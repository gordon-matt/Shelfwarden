// shelfwardenChapterPicker — renders PDF pages as a single scrollable column inside the audiobook
// section editor and lets the user click the gap between pages to mark where a chapter begins.
// Boundaries (the page numbers that start a new chapter) are reported back to Blazor on every
// change. Uses the same self-hosted pdf.js build as the reader (wwwroot/lib/pdfjs).

(function () {
    if (window.shelfwardenChapterPicker) return;

    var state = {
        doc: null,
        container: null,
        dotnetRef: null,
        pageCount: 0,
        /** @type {Set<number>} Page numbers (>= 2) that start a new chapter. */
        boundaries: new Set(),
        /** @type {Map<number, {wrapper: HTMLElement, marker: HTMLElement|null}>} */
        pageElements: new Map(),
        rendered: new Set(),
        observer: null,
        renderQueue: Promise.resolve(),
        scriptLoaded: false,
        /** When true, pages render at reduced resolution so scrolling stays fast. */
        lowRes: false,
        /** Max display width (CSS px) of each page thumbnail. */
        maxWidth: 440,
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

    async function ensurePdfJsLoaded() {
        if (window.pdfjsLib) {
            if (window.pdfjsLib.GlobalWorkerOptions && !window.pdfjsLib.GlobalWorkerOptions.workerSrc) {
                window.pdfjsLib.GlobalWorkerOptions.workerSrc = 'lib/pdfjs/build/pdf.worker.min.js';
            }
            return;
        }
        await loadScript('lib/pdfjs/build/pdf.min.js');
        if (window.pdfjsLib && window.pdfjsLib.GlobalWorkerOptions) {
            window.pdfjsLib.GlobalWorkerOptions.workerSrc = 'lib/pdfjs/build/pdf.worker.min.js';
        }
        state.scriptLoaded = true;
    }

    function notifyBoundaries() {
        if (!state.dotnetRef) return;
        var pages = Array.from(state.boundaries).sort(function (a, b) { return a - b; });
        try {
            state.dotnetRef.invokeMethodAsync('OnBoundariesChanged', pages);
        } catch (err) {
            console.warn('chapterPicker: boundary push failed', err);
        }
    }

    /** Refresh every marker's active state + label after the boundary set changes. */
    function refreshMarkers() {
        var sorted = Array.from(state.boundaries).sort(function (a, b) { return a - b; });
        state.pageElements.forEach(function (entry, pageNum) {
            var marker = entry.marker;
            if (!marker) return;
            var active = state.boundaries.has(pageNum);
            marker.classList.toggle('active', active);
            var label = marker.querySelector('.cp-marker-label');
            if (!label) return;
            if (active) {
                var idx = sorted.indexOf(pageNum); // 0-based among chapter starts
                label.innerHTML = '<i class="bi bi-bookmark-fill"></i> Chapter ' + (idx + 1) + ' starts here';
            } else {
                label.innerHTML = '<i class="bi bi-plus-circle"></i> Insert chapter break';
            }
        });
    }

    function toggleBoundary(pageNum) {
        if (pageNum < 2 || pageNum > state.pageCount) return;
        if (state.boundaries.has(pageNum)) {
            state.boundaries.delete(pageNum);
        } else {
            state.boundaries.add(pageNum);
        }
        refreshMarkers();
        notifyBoundaries();
    }

    async function renderPage(pageNum) {
        if (!state.doc || state.rendered.has(pageNum)) return;
        state.rendered.add(pageNum);

        state.renderQueue = state.renderQueue.then(async function () {
            var entry = state.pageElements.get(pageNum);
            if (!entry || !state.container) return;
            try {
                var page = await state.doc.getPage(pageNum);
                // Low-res mode renders well below device density: the marker view only needs
                // enough detail to spot chapter starts, and fewer pixels means faster scrolling.
                var dpr = state.lowRes ? 0.6 : Math.min(window.devicePixelRatio || 1, 2);
                var available = Math.max(120, state.container.clientWidth - 48);
                // Cap thumbnail width so very wide viewports don't render huge canvases.
                available = Math.min(available, state.maxWidth);
                var unscaled = page.getViewport({ scale: 1 });
                var fitScale = available / unscaled.width;
                var viewport = page.getViewport({ scale: fitScale * dpr });

                var canvas = document.createElement('canvas');
                canvas.width = viewport.width;
                canvas.height = viewport.height;
                canvas.style.width = (viewport.width / dpr) + 'px';
                canvas.style.height = (viewport.height / dpr) + 'px';
                await page.render({ canvasContext: canvas.getContext('2d'), viewport: viewport }).promise;

                var holder = entry.wrapper.querySelector('.cp-canvas');
                if (holder) {
                    holder.innerHTML = '';
                    holder.appendChild(canvas);
                }
            } catch (err) {
                console.warn('chapterPicker: failed to render page ' + pageNum, err);
                state.rendered.delete(pageNum);
            }
        });
    }

    function buildPlaceholders() {
        state.container.innerHTML = '';
        state.pageElements.clear();
        state.rendered.clear();

        for (var i = 1; i <= state.pageCount; i++) {
            var pageNum = i;
            var wrapper = document.createElement('div');
            wrapper.className = 'cp-page';
            wrapper.dataset.page = pageNum;

            var marker = null;
            if (pageNum === 1) {
                var start = document.createElement('div');
                start.className = 'cp-marker cp-marker-start';
                start.innerHTML = '<span class="cp-marker-label"><i class="bi bi-bookmark-fill"></i> Beginning of document</span>';
                wrapper.appendChild(start);
            } else {
                marker = document.createElement('button');
                marker.type = 'button';
                marker.className = 'cp-marker';
                marker.innerHTML = '<span class="cp-marker-label"><i class="bi bi-plus-circle"></i> Insert chapter break</span>';
                (function (p) {
                    marker.addEventListener('click', function () { toggleBoundary(p); });
                })(pageNum);
                wrapper.appendChild(marker);
            }

            var canvasHolder = document.createElement('div');
            canvasHolder.className = 'cp-canvas';
            var ph = document.createElement('div');
            ph.className = 'cp-canvas-placeholder';
            canvasHolder.appendChild(ph);
            wrapper.appendChild(canvasHolder);

            var label = document.createElement('div');
            label.className = 'cp-page-num';
            label.textContent = 'Page ' + pageNum + ' / ' + state.pageCount;
            wrapper.appendChild(label);

            state.container.appendChild(wrapper);
            state.pageElements.set(pageNum, { wrapper: wrapper, marker: marker });
        }

        refreshMarkers();
    }

    function setupObserver() {
        if (state.observer) state.observer.disconnect();
        state.observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (!entry.isIntersecting) return;
                var pageNum = parseInt(entry.target.dataset.page, 10);
                renderPage(pageNum);
                if (pageNum + 1 <= state.pageCount) renderPage(pageNum + 1);
            });
        }, { root: state.container, threshold: 0.05 });

        state.pageElements.forEach(function (entry) {
            state.observer.observe(entry.wrapper);
        });
    }

    window.shelfwardenChapterPicker = {
        /**
         * Mount the picker into the given container and load the PDF.
         * @param {string} containerId
         * @param {string} url — PDF byte stream URL (range-enabled).
         * @param {object} dotnetRef — receives OnBoundariesChanged(int[]).
         * @param {number[]} initialBoundaries — page numbers to pre-mark as chapter starts.
         * @param {{lowRes?:boolean, maxWidth?:number}} [options] — render tuning.
         * @returns {Promise<{pageCount:number}>}
         */
        mount: async function (containerId, url, dotnetRef, initialBoundaries, options) {
            await ensurePdfJsLoaded();
            this.dispose();

            var container = document.getElementById(containerId);
            if (!container) {
                console.warn('chapterPicker.mount: container not found', containerId);
                return { pageCount: 0 };
            }

            options = options || {};
            state.lowRes = !!options.lowRes;
            state.maxWidth = options.maxWidth && options.maxWidth > 0 ? options.maxWidth : 440;

            state.container = container;
            state.dotnetRef = dotnetRef;
            state.boundaries = new Set();
            (initialBoundaries || []).forEach(function (p) {
                if (p >= 2) state.boundaries.add(p);
            });

            try {
                state.doc = await window.pdfjsLib.getDocument(url).promise;
            } catch (err) {
                console.warn('chapterPicker.mount: failed to load PDF', err);
                container.innerHTML = '<div class="text-secondary p-3 small">Couldn\'t open this PDF for marking.</div>';
                return { pageCount: 0 };
            }

            state.pageCount = state.doc.numPages;
            // Drop any seeded boundaries beyond the real page count.
            state.boundaries.forEach(function (p) {
                if (p > state.pageCount) state.boundaries.delete(p);
            });

            buildPlaceholders();
            setupObserver();
            return { pageCount: state.pageCount };
        },

        getBoundaries: function () {
            return Array.from(state.boundaries).sort(function (a, b) { return a - b; });
        },

        /**
         * Toggle low-resolution rendering on the fly and redraw already-rendered pages so the
         * change is visible without reloading the PDF.
         * @param {boolean} lowRes
         */
        setLowRes: function (lowRes) {
            state.lowRes = !!lowRes;
            if (!state.doc) return;

            var toRedraw = Array.from(state.rendered);
            state.rendered.clear();
            state.pageElements.forEach(function (entry) {
                var holder = entry.wrapper.querySelector('.cp-canvas');
                if (holder) {
                    holder.innerHTML = '<div class="cp-canvas-placeholder"></div>';
                }
            });
            toRedraw.forEach(function (p) { renderPage(p); });
        },

        dispose: function () {
            if (state.observer) {
                state.observer.disconnect();
                state.observer = null;
            }
            if (state.doc) {
                try { state.doc.destroy(); } catch (e) { }
                state.doc = null;
            }
            if (state.container) {
                state.container.innerHTML = '';
            }
            state.container = null;
            state.dotnetRef = null;
            state.pageCount = 0;
            state.boundaries = new Set();
            state.pageElements.clear();
            state.rendered.clear();
            state.renderQueue = Promise.resolve();
        },
    };
})();
