// Reader-specific JS interop. EPUB rendering is delegated to epub.js, PDF rendering to
// pdf.js — both libraries are self-hosted under wwwroot/lib (see libman.json) so the
// reader works offline and we don't depend on any CDN's uptime. Blazor stays the source
// of truth for navigation / progress save scheduling; this file is the thin glue that
// translates between Blazor calls and the underlying viewer libraries.

(function () {
    if (window.shelfwardenReader) return;

    var state = {
        epub: null,
        rendition: null,
        dotnetRef: null,
        scriptLoaded: false,
        pendingProgressTimer: null,
        currentCfi: null,
        currentPercent: 0,

        // PDF state
        pdfScriptLoaded: false,
        pdfDoc: null,
        pdfContainerId: null,
        pdfContainer: null,
        pdfPageCount: 0,
        pdfCurrentPage: 1,
        pdfPageObserver: null,
        pdfPageElements: new Map(),
        pdfRenderedPages: new Set(),
        pdfDotnetRef: null,
        pdfRenderQueue: Promise.resolve(),
    };

    function loadScript(src) {
        return new Promise(function (resolve, reject) {
            if (document.querySelector('script[data-src="' + src + '"]')) {
                resolve();
                return;
            }
            var script = document.createElement('script');
            script.src = src;
            script.async = true;
            script.dataset.src = src;
            script.onload = function () { resolve(); };
            script.onerror = function () { reject(new Error('Failed to load ' + src)); };
            document.head.appendChild(script);
        });
    }

    async function ensureEpubJsLoaded() {
        if (state.scriptLoaded && window.ePub) return;
        // We self-host both libraries under wwwroot/lib (managed by libman.json) so the reader
        // works offline and isn't subject to CDN availability — cdnjs only stocks epub.js up
        // to 0.2.15 which lacks the modern Rendition API we depend on.
        await loadScript('lib/jszip/dist/jszip.min.js');
        await loadScript('lib/epubjs/dist/epub.min.js');
        state.scriptLoaded = true;
    }

    async function ensurePdfJsLoaded() {
        if (state.pdfScriptLoaded && window.pdfjsLib) return;
        await loadScript('lib/pdfjs/build/pdf.min.js');
        // pdf.js needs an explicit pointer to its worker script. We host the worker file
        // alongside the main library so the same origin / cache-control policy applies.
        if (window.pdfjsLib && window.pdfjsLib.GlobalWorkerOptions) {
            window.pdfjsLib.GlobalWorkerOptions.workerSrc = 'lib/pdfjs/build/pdf.worker.min.js';
        }
        state.pdfScriptLoaded = true;
    }

    function scheduleProgressPush(percent, cfi) {
        if (!state.dotnetRef) return;
        if (state.pendingProgressTimer) clearTimeout(state.pendingProgressTimer);
        state.pendingProgressTimer = setTimeout(function () {
            try {
                state.dotnetRef.invokeMethodAsync('OnProgress', percent, null, cfi || null);
            } catch (err) {
                console.warn('Progress push failed', err);
            }
        }, 750);
    }

    var pdfProgressTimer = null;
    function schedulePdfProgressPush() {
        if (!state.pdfDotnetRef || !state.pdfPageCount) return;
        if (pdfProgressTimer) clearTimeout(pdfProgressTimer);
        pdfProgressTimer = setTimeout(function () {
            var percent = Math.round((state.pdfCurrentPage / state.pdfPageCount) * 100);
            try {
                state.pdfDotnetRef.invokeMethodAsync('OnProgress', percent, state.pdfCurrentPage, null);
            } catch (err) {
                console.warn('PDF progress push failed', err);
            }
        }, 750);
    }

    /**
     * Render one PDF page onto its placeholder canvas. Pages render lazily as they scroll into
     * view because rendering every page up-front is prohibitively expensive on large PDFs.
     */
    async function renderPdfPage(pageNum) {
        if (!state.pdfDoc || state.pdfRenderedPages.has(pageNum)) return;
        state.pdfRenderedPages.add(pageNum);

        // Serialise renders so a fast scroll doesn't queue 500 concurrent canvas allocations
        // (pdf.js will happily try and OOM the tab).
        state.pdfRenderQueue = state.pdfRenderQueue.then(async function () {
            var pageWrapper = state.pdfPageElements.get(pageNum);
            if (!pageWrapper) return;

            try {
                var page = await state.pdfDoc.getPage(pageNum);
                var canvas = pageWrapper.querySelector('canvas');
                if (!canvas) return;

                // Scale to fit the container width while honouring devicePixelRatio so the
                // page is sharp on hi-DPI screens.
                var dpr = window.devicePixelRatio || 1;
                var availableWidth = state.pdfContainer.clientWidth - 32;
                var unscaledViewport = page.getViewport({ scale: 1 });
                var scale = Math.max(0.5, availableWidth / unscaledViewport.width);
                var viewport = page.getViewport({ scale: scale * dpr });

                canvas.width = viewport.width;
                canvas.height = viewport.height;
                canvas.style.width = (viewport.width / dpr) + 'px';
                canvas.style.height = (viewport.height / dpr) + 'px';

                var ctx = canvas.getContext('2d');
                await page.render({ canvasContext: ctx, viewport: viewport }).promise;
            } catch (err) {
                console.warn('Failed to render PDF page ' + pageNum, err);
                state.pdfRenderedPages.delete(pageNum);
            }
        });
    }

    function buildPdfPagePlaceholders() {
        state.pdfContainer.innerHTML = '';
        state.pdfPageElements.clear();
        state.pdfRenderedPages.clear();

        for (var i = 1; i <= state.pdfPageCount; i++) {
            var pageWrapper = document.createElement('div');
            pageWrapper.className = 'pdf-page';
            pageWrapper.dataset.page = i;

            var canvas = document.createElement('canvas');
            pageWrapper.appendChild(canvas);

            var label = document.createElement('div');
            label.className = 'pdf-page-label';
            label.textContent = 'Page ' + i + ' / ' + state.pdfPageCount;
            pageWrapper.appendChild(label);

            state.pdfContainer.appendChild(pageWrapper);
            state.pdfPageElements.set(i, pageWrapper);
        }
    }

    function setupPdfObserver() {
        if (state.pdfPageObserver) state.pdfPageObserver.disconnect();

        // 0.5 means a page becomes "current" once its midpoint crosses the viewport
        // midpoint, which matches what users intuitively think of as the current page.
        state.pdfPageObserver = new IntersectionObserver(function (entries) {
            // Among visible pages, pick the one closest to the viewport centre.
            var bestPage = null;
            var bestRatio = 0;
            entries.forEach(function (entry) {
                if (entry.intersectionRatio > bestRatio) {
                    bestRatio = entry.intersectionRatio;
                    bestPage = parseInt(entry.target.dataset.page, 10);
                }
                if (entry.isIntersecting) {
                    var pageNum = parseInt(entry.target.dataset.page, 10);
                    renderPdfPage(pageNum);
                    // Pre-render the next/previous page so smooth scrolling doesn't show a
                    // blank page momentarily.
                    if (pageNum + 1 <= state.pdfPageCount) renderPdfPage(pageNum + 1);
                    if (pageNum - 1 >= 1) renderPdfPage(pageNum - 1);
                }
            });
            if (bestPage && bestPage !== state.pdfCurrentPage) {
                state.pdfCurrentPage = bestPage;
                schedulePdfProgressPush();
            }
        }, {
            root: state.pdfContainer,
            threshold: [0.1, 0.5, 0.9],
        });

        state.pdfPageElements.forEach(function (el) {
            state.pdfPageObserver.observe(el);
        });
    }

    window.shelfwardenReader = {
        /**
         * Mount epub.js into the given container element, load the book at `url`, and start
         * pushing progress updates back into the Blazor component via the supplied .NET ref.
         */
        mountEpub: async function (containerId, url, dotnetRef, resumeCfi) {
            await ensureEpubJsLoaded();
            this.disposeEpub();

            state.dotnetRef = dotnetRef;
            state.epub = window.ePub(url, { openAs: 'epub' });
            state.rendition = state.epub.renderTo(containerId, {
                width: '100%',
                height: '100%',
                manager: 'default',
                flow: 'paginated',
                spread: 'auto',
            });

            await state.rendition.display(resumeCfi || undefined);

            state.rendition.on('relocated', function (location) {
                var percent = 0;
                if (state.epub && state.epub.locations && location && location.start && state.epub.locations.length()) {
                    percent = Math.round(state.epub.locations.percentageFromCfi(location.start.cfi) * 100);
                }
                var cfi = location && location.start ? location.start.cfi : null;
                state.currentCfi = cfi;
                state.currentPercent = percent;
                scheduleProgressPush(percent, cfi);
            });

            // epub.js needs a separate pass over the spine to compute character offsets so
            // that "% complete" makes sense. The 1024 is char-width per logical chunk —
            // larger values mean fewer locations but lower precision.
            await state.epub.ready;
            await state.epub.locations.generate(1024);
        },

        nextPage: function () { if (state.rendition) state.rendition.next(); },
        prevPage: function () { if (state.rendition) state.rendition.prev(); },

        /**
         * Returns { cfi, percent } for the EPUB's current spot. Blazor calls this when the
         * user clicks "Bookmark this spot" so we can attach the location server-side.
         */
        getEpubLocation: function () {
            return { cfi: state.currentCfi, percent: state.currentPercent };
        },

        /**
         * Jump the EPUB rendition to the given CFI. Used by the bookmark list when the user
         * clicks an entry. Returns true on success.
         */
        gotoEpub: function (cfi) {
            if (!state.rendition || !cfi) return false;
            try {
                state.rendition.display(cfi);
                return true;
            } catch (err) {
                console.warn('gotoEpub failed', err);
                return false;
            }
        },

        disposeEpub: function () {
            if (state.pendingProgressTimer) {
                clearTimeout(state.pendingProgressTimer);
                state.pendingProgressTimer = null;
            }
            if (state.rendition) {
                try { state.rendition.destroy(); } catch (e) { }
                state.rendition = null;
            }
            if (state.epub) {
                try { state.epub.destroy(); } catch (e) { }
                state.epub = null;
            }
            state.dotnetRef = null;
        },

        /**
         * Mount pdf.js into the given container. Pages are rendered lazily as they scroll into
         * view (cheap on huge PDFs) and the current page is reported back to Blazor whenever
         * it changes so we can persist reading progress and pick the right "current page" for
         * bookmarks. Resumes from `resumePage` if provided.
         */
        mountPdf: async function (containerId, url, dotnetRef, resumePage) {
            await ensurePdfJsLoaded();
            this.disposePdf();

            var container = document.getElementById(containerId);
            if (!container) {
                console.warn('mountPdf: container not found', containerId);
                return { pageCount: 0 };
            }

            state.pdfContainerId = containerId;
            state.pdfContainer = container;
            state.pdfDotnetRef = dotnetRef;
            state.pdfCurrentPage = 1;

            try {
                state.pdfDoc = await window.pdfjsLib.getDocument(url).promise;
            } catch (err) {
                console.warn('mountPdf: failed to load PDF', err);
                container.innerHTML = '<div class="empty-state"><i class="bi bi-file-earmark-x empty-icon"></i><h3>Couldn\'t open this PDF</h3></div>';
                return { pageCount: 0 };
            }

            state.pdfPageCount = state.pdfDoc.numPages;
            buildPdfPagePlaceholders();
            setupPdfObserver();

            if (resumePage && resumePage > 1 && resumePage <= state.pdfPageCount) {
                // Wait a tick so the placeholders have been laid out before scrollIntoView.
                setTimeout(function () {
                    var el = state.pdfPageElements.get(resumePage);
                    if (el) el.scrollIntoView({ block: 'start' });
                }, 0);
            }

            return { pageCount: state.pdfPageCount };
        },

        /**
         * Returns the active PDF page number. Used by the bookmark button so the caller
         * doesn't have to ask the user.
         */
        getPdfPage: function () {
            return state.pdfCurrentPage || 1;
        },

        /**
         * Scroll a specific page into view. Triggers the lazy renderer too so the destination
         * page is ready by the time the scroll lands.
         */
        gotoPdfPage: function (pageNumber) {
            if (!state.pdfPageElements.size || !pageNumber) return false;
            var clamped = Math.max(1, Math.min(state.pdfPageCount, pageNumber));
            var el = state.pdfPageElements.get(clamped);
            if (!el) return false;
            renderPdfPage(clamped);
            el.scrollIntoView({ block: 'start', behavior: 'smooth' });
            return true;
        },

        nextPdfPage: function () {
            return this.gotoPdfPage((state.pdfCurrentPage || 1) + 1);
        },

        prevPdfPage: function () {
            return this.gotoPdfPage((state.pdfCurrentPage || 1) - 1);
        },

        disposePdf: function () {
            if (pdfProgressTimer) { clearTimeout(pdfProgressTimer); pdfProgressTimer = null; }
            if (state.pdfPageObserver) {
                state.pdfPageObserver.disconnect();
                state.pdfPageObserver = null;
            }
            if (state.pdfDoc) {
                try { state.pdfDoc.destroy(); } catch (e) { }
                state.pdfDoc = null;
            }
            if (state.pdfContainer) {
                state.pdfContainer.innerHTML = '';
            }
            state.pdfContainer = null;
            state.pdfContainerId = null;
            state.pdfPageCount = 0;
            state.pdfCurrentPage = 1;
            state.pdfPageElements.clear();
            state.pdfRenderedPages.clear();
            state.pdfDotnetRef = null;
            state.pdfRenderQueue = Promise.resolve();
        },
    };
})();
