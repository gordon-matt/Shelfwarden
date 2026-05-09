// Reader-specific JS interop. EPUB rendering is delegated to epub.js, PDF rendering to
// pdf.js — both libraries are self-hosted under wwwroot/lib (see libman.json) so the
// reader works offline and we don't depend on any CDN's uptime. Blazor stays the source
// of truth for navigation / progress save scheduling; this file is the thin glue that
// translates between Blazor calls and the underlying viewer libraries.
//
// Architecture: only the inner `.reader-document-content` node scrolls and receives zoom;
// toolbar / sidebar chrome stay in Blazor at fixed CSS size (no transform/zoom on shells).
//
// /**
//  * @typedef {Object} ShelfwardenReaderAdapter
//  * @property {'epub'|'pdf'} format
//  * @property {function(): void} zoomIn
//  * @property {function(): void} zoomOut
//  * @property {function(): void} resetZoom
//  * @property {function(number): void} setScale — PDF: multiplier on fit-width; EPUB: font scale (1 = 100%)
//  */

(function () {
    if (window.shelfwardenReader) return;

    /** User multiplier on top of fit-to-viewport base scale (whole page visible at 1.0). */
    var PDF_ZOOM_MIN = 0.12;
    var PDF_ZOOM_MAX = 3;
    var PDF_ZOOM_STEP = 0.12;
    var EPUB_PROGRESS_DEBOUNCE_MS = 650;
    var EPUB_FONT_MIN = 0.75;
    var EPUB_FONT_MAX = 2.25;
    var EPUB_FONT_STEP = 0.1;

    // Serialize EPUB opens so a new mount never runs while the previous book is still
    // tearing down — overlapping unpack() calls corrupt epub.js (this.resources undefined).
    var epubMountChain = Promise.resolve();

    /** Serializes typography + display(cfi) + next/prev so rendition.manager is never used mid-display. */
    var epubRenditionChain = Promise.resolve();

    function createEpubMountGate() {
        var gate = {};
        gate.promise = new Promise(function (resolve) {
            gate.resolve = resolve;
        });
        return gate;
    }

    var state = {
        epub: null,
        rendition: null,
        epubContainerId: null,
        epubSessionId: 0,
        epubLocationsReady: false,
        /** Resolves when EPUB mount (display, locations, seed, initial typography) is finished — next/prev must await this. */
        epubMountGate: (function () {
            var g = createEpubMountGate();
            g.resolve();
            return g;
        })(),
        /** First EPUB progress push in this session is sent immediately so Blazor/UI and DB update without waiting for debounce. */
        epubHadFirstProgressPush: false,
        /** Ignore relocated until mount seeding finishes — early events used to force OnProgress(0) before locations exist. */
        epubMountSeedDone: false,
        dotnetRef: null,
        scriptLoaded: false,
        pendingProgressTimer: null,
        currentCfi: null,
        currentPercent: 0,
        /** @type {number} Typography scale; 1 = 100% body font (EPUB reflow). */
        epubFontScale: 1,

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
        /** @type {number} Multiplier applied on top of fit-to-width pdf.js viewport scale. */
        pdfUserScale: 1,
        /** @type {function(): void|null} */
        pdfScrollListener: null,
    };

    var pdfResizeTimer = null;

    function clamp(n, lo, hi) {
        return Math.min(hi, Math.max(lo, n));
    }

    /**
     * @returns {ShelfwardenReaderAdapter|null}
     */
    function getActiveAdapter() {
        if (state.rendition) {
            return {
                format: 'epub',
                zoomIn: function () { adjustEpubFontScale(1); },
                zoomOut: function () { adjustEpubFontScale(-1); },
                resetZoom: function () { setEpubFontScale(1); },
                setScale: function (s) { setEpubFontScale(s); },
            };
        }
        if (state.pdfDoc && state.pdfContainer) {
            return {
                format: 'pdf',
                zoomIn: function () { adjustPdfUserScale(1); },
                zoomOut: function () { adjustPdfUserScale(-1); },
                resetZoom: function () { setPdfUserScale(1); },
                setScale: function (s) { setPdfUserScale(s); },
            };
        }
        return null;
    }

    function setPdfUserScale(s) {
        state.pdfUserScale = clamp(s, PDF_ZOOM_MIN, PDF_ZOOM_MAX);
        refreshPdfZoom();
    }

    function adjustPdfUserScale(direction) {
        setPdfUserScale(state.pdfUserScale + direction * PDF_ZOOM_STEP);
    }

    function setEpubFontScale(s) {
        state.epubFontScale = clamp(s, EPUB_FONT_MIN, EPUB_FONT_MAX);
        applyEpubTypography();
    }

    function adjustEpubFontScale(direction) {
        setEpubFontScale(state.epubFontScale + direction * EPUB_FONT_STEP);
    }

    function applyEpubTypography() {
        var sessionId = state.epubSessionId;
        epubRenditionChain = epubRenditionChain.then(async function () {
            await state.epubMountGate.promise;
            if (sessionId !== state.epubSessionId) return;
            return applyEpubTypographyAsync(sessionId);
        }).catch(function (err) {
            console.warn('EPUB typography chain', err);
        });
    }

    /**
     * epub.js defines r.next on the prototype before start() creates r.manager; calling next() then throws.
     * Wait for started + verify manager before navigation.
     */
    async function whenEpubManagerReady(r, sessionId) {
        if (!r || sessionId !== state.epubSessionId) return false;
        if (r.started && typeof r.started.then === 'function') {
            await r.started;
        }
        if (sessionId !== state.epubSessionId) return false;
        return !!(r.manager && r.book);
    }

    function detachRelocatedListener(r, handler) {
        if (!r || !handler) return;
        try {
            if (typeof r.off === 'function') {
                r.off('relocated', handler);
            } else if (typeof r.removeListener === 'function') {
                r.removeListener('relocated', handler);
            }
        } catch (e) { /* ignore */ }
    }

    /**
     * Ensures currentCfi matches the visible spread after next/prev (avoids typography using a stale CFI).
     */
    function waitForRelocated(r, ms) {
        return new Promise(function (resolve) {
            if (!r) {
                resolve();
                return;
            }
            var done = false;
            var timer = setTimeout(finish, ms || 900);
            function finish() {
                if (done) return;
                done = true;
                clearTimeout(timer);
                detachRelocatedListener(r, handler);
                resolve();
            }
            function handler() {
                finish();
            }
            r.on('relocated', handler);
        });
    }

    async function applyEpubTypographyAsync(sessionId) {
        if (sessionId !== state.epubSessionId) return;
        var r = state.rendition;
        if (!r) return;
        var pct = Math.round(100 * state.epubFontScale);
        var size = pct + '%';
        try {
            if (r.themes && typeof r.themes.fontSize === 'function') {
                r.themes.fontSize(size);
            } else if (r.themes && typeof r.themes.default === 'function') {
                r.themes.default({ body: { 'font-size': size + ' !important' } });
            }
        } catch (err) {
            console.warn('EPUB typography update failed', err);
        }
        try {
            if (typeof r.resize === 'function') {
                r.resize();
            }
        } catch (e) { /* ignore */ }
        // Never call rendition.display() here — it reloads the spread and was causing random
        // jumps back + visible flicker. Font + resize must preserve the current location.
    }

    function normalizeLocationsPercentage(raw) {
        if (raw == null || typeof raw !== 'number' || isNaN(raw)) return null;
        if (raw >= 0 && raw <= 1) return Math.round(raw * 100);
        if (raw > 1 && raw <= 100) return Math.round(raw);
        return null;
    }

    function spineProgressPercentFromLocation(book, location) {
        if (!book || !book.spine || !location || !location.start) return null;
        var spine = book.spine;
        var len = typeof spine.length === 'number' ? spine.length : null;
        if (!len && spine.items && typeof spine.items.length === 'number') {
            len = spine.items.length;
        }
        if (!len || len < 1) return null;

        var idx = location.start.index;
        if (typeof idx === 'number' && idx >= 0) {
            var disp = location.start.displayed;
            var page = disp && typeof disp.page === 'number' ? disp.page : 1;
            var total = disp && typeof disp.total === 'number' && disp.total > 0 ? disp.total : 1;
            var within = (page - 1 + 0.35) / total;
            var frac = (idx + clamp(within, 0, 1)) / len;
            return Math.round(clamp(frac, 0, 1) * 100);
        }

        var href = location.start.href;
        if (!href) return null;
        var hrefNorm = String(href).split('#')[0].split('/').pop() || '';
        for (var i = 0; i < len; i++) {
            var item = typeof spine.get === 'function' ? spine.get(i) : spine.items[i];
            if (!item) continue;
            var ih = item.href || '';
            var itemBase = String(ih).split('#')[0].split('/').pop() || '';
            if (!itemBase) continue;
            if (href === ih || href.indexOf(itemBase) >= 0 || itemBase === hrefNorm
                || hrefNorm.indexOf(itemBase) >= 0 || ih.indexOf(hrefNorm) >= 0) {
                return Math.round(((i + 0.35) / len) * 100);
            }
        }
        return null;
    }

    function computeEpubProgressPercent(book, location) {
        if (!location || !location.start) return 0;
        var sp = location.start.percentage;
        if (typeof sp === 'number' && !isNaN(sp)) {
            var fromRendition = normalizeLocationsPercentage(sp);
            if (fromRendition != null) return clamp(fromRendition, 0, 100);
        }
        var cfi = location.start.cfi;
        var fromLoc = null;
        if (state.epubLocationsReady && book && book.locations && cfi) {
            try {
                fromLoc = normalizeLocationsPercentage(book.locations.percentageFromCfi(cfi));
            } catch (e) { /* ignore */ }
        }
        if (fromLoc != null) return clamp(fromLoc, 0, 100);
        var fromSpine = spineProgressPercentFromLocation(book, location);
        if (fromSpine != null) return clamp(fromSpine, 0, 100);
        return 0;
    }

    /**
     * epub.js often leaves rendition.currentLocation() as undefined when the manager returns a
     * Promise (the library never returns that Promise). After locations.generate, reportLocation()
     * fills rendition.location — use that to seed progress so Blazor is not stuck at 0%.
     */
    async function seedEpubProgressAfterMount(sessionId) {
        var r = state.rendition;
        var book = state.epub;
        if (!r || !book || sessionId !== state.epubSessionId) return;
        try {
            if (typeof r.reportLocation === 'function') {
                r.reportLocation();
            }
            await new Promise(function (resolve) { setTimeout(resolve, 0); });
            var loc = null;
            for (var attempt = 0; attempt < 16; attempt++) {
                await new Promise(function (resolve) { requestAnimationFrame(resolve); });
                if (sessionId !== state.epubSessionId) return;
                loc = r.location;
                if (loc && loc.start && loc.start.cfi) break;
            }
            if (!loc || !loc.start || !loc.start.cfi) return;
            var cfi = loc.start.cfi;
            var percent = computeEpubProgressPercent(book, loc);
            state.currentCfi = cfi;
            state.currentPercent = percent;
            scheduleProgressPush(percent, cfi);
        } catch (e) {
            console.warn('EPUB progress seed failed', e);
        }
    }

    function invalidateAllPdfRenders() {
        state.pdfRenderedPages.clear();
        state.pdfPageElements.forEach(function (wrapper) {
            var h = wrapper.offsetHeight;
            if (h > 0) {
                wrapper.style.minHeight = h + 'px';
            }
            var c = wrapper.querySelector('canvas');
            if (c) {
                c.width = 0;
                c.height = 0;
                c.style.width = '';
                c.style.height = '';
            }
        });
    }

    function rerenderVisiblePdfPages() {
        if (!state.pdfDoc || !state.pdfContainer) return;
        var el = state.pdfContainer;
        var rootRect = el.getBoundingClientRect();
        state.pdfPageElements.forEach(function (wrapper, pageNum) {
            var rect = wrapper.getBoundingClientRect();
            if (rect.bottom > rootRect.top - 120 && rect.top < rootRect.bottom + 120) {
                state.pdfRenderedPages.delete(pageNum);
                renderPdfPage(pageNum);
            }
        });
    }

    /**
     * Derive the active page from scroll geometry (not IntersectionObserver batches — those
     * only report *changed* entries, which wrongly jumps the current page after zoom/layout).
     */
    function syncPdfCurrentPageFromViewport() {
        if (!state.pdfDoc || !state.pdfContainer || !state.pdfPageCount) return;
        var root = state.pdfContainer;
        var st = root.scrollTop;
        var ch = root.clientHeight;
        if (ch <= 0) return;

        var anchorY = st + ch * 0.38;
        var bestPage = state.pdfCurrentPage || 1;
        var bestVis = -1;
        var bestDist = Infinity;

        state.pdfPageElements.forEach(function (wrapper, pageNum) {
            var top = wrapper.offsetTop;
            var h = wrapper.offsetHeight || 1;
            var bottom = top + h;
            var visTop = Math.max(top, st);
            var visBot = Math.min(bottom, st + ch);
            var vis = Math.max(0, visBot - visTop);
            var mid = (top + bottom) / 2;
            var dist = Math.abs(mid - anchorY);

            if (vis > bestVis + 0.5) {
                bestVis = vis;
                bestDist = dist;
                bestPage = pageNum;
            } else if (Math.abs(vis - bestVis) <= 0.5 && vis > 0 && dist < bestDist) {
                bestDist = dist;
                bestPage = pageNum;
            }
        });

        if (bestPage !== state.pdfCurrentPage) {
            state.pdfCurrentPage = bestPage;
            schedulePdfProgressPush();
        }
    }

    var pdfScrollSyncTimer = null;
    function schedulePdfScrollSync() {
        if (pdfScrollSyncTimer) clearTimeout(pdfScrollSyncTimer);
        pdfScrollSyncTimer = setTimeout(function () {
            pdfScrollSyncTimer = null;
            syncPdfCurrentPageFromViewport();
        }, 40);
    }

    function refreshPdfZoom() {
        if (!state.pdfDoc || !state.pdfContainer) return;
        var el = state.pdfContainer;

        syncPdfCurrentPageFromViewport();
        var anchorPage = clamp(state.pdfCurrentPage || 1, 1, state.pdfPageCount);
        var wrapper = state.pdfPageElements.get(anchorPage);
        var oldH = wrapper && wrapper.offsetHeight > 0 ? wrapper.offsetHeight : 1;
        var yInPage = wrapper ? clamp(el.scrollTop - wrapper.offsetTop, 0, Math.max(0, oldH - 1)) : 0;
        var fraction = oldH > 0 ? yInPage / oldH : 0;

        invalidateAllPdfRenders();

        renderPdfPage(anchorPage);
        if (anchorPage > 1) renderPdfPage(anchorPage - 1);
        if (anchorPage < state.pdfPageCount) renderPdfPage(anchorPage + 1);
        rerenderVisiblePdfPages();

        state.pdfRenderQueue = state.pdfRenderQueue.then(function () {
            var w = state.pdfPageElements.get(anchorPage);
            if (w && el) {
                var nh = Math.max(1, w.offsetHeight);
                var target = w.offsetTop + fraction * nh;
                el.scrollTop = clamp(target, 0, Math.max(0, el.scrollHeight - el.clientHeight));
            }
            return new Promise(function (resolve) {
                requestAnimationFrame(function () {
                    syncPdfCurrentPageFromViewport();
                    resolve();
                });
            });
        });
    }

    function schedulePdfLayoutRefresh() {
        if (!state.pdfDoc || !state.pdfContainer) return;
        clearTimeout(pdfResizeTimer);
        pdfResizeTimer = setTimeout(function () {
            refreshPdfZoom();
        }, 120);
    }

    if (typeof window !== 'undefined') {
        window.addEventListener('resize', schedulePdfLayoutRefresh);
    }

    function touchPairDistance(t0, t1) {
        var dx = t0.clientX - t1.clientX;
        var dy = t0.clientY - t1.clientY;
        return Math.sqrt(dx * dx + dy * dy);
    }

    var savedViewportContent = null;

    /**
     * Optional viewport hint while the reader is open (restored on dispose). Helps mobile
     * layouts without permanently locking user zoom for the whole app.
     */
    function applyReaderChromeViewport(active) {
        var m = document.querySelector('meta[name="viewport"]');
        if (!m) return;
        if (active) {
            if (savedViewportContent === null) {
                savedViewportContent = m.getAttribute('content') || '';
            }
            m.setAttribute('content',
                'width=device-width, initial-scale=1, viewport-fit=cover, interactive-widget=resizes-content');
        } else if (savedViewportContent !== null) {
            m.setAttribute('content', savedViewportContent);
            savedViewportContent = null;
        }
    }

    /**
     * Ctrl+wheel (trackpad pinch) + two-finger pinch: PDF updates live; EPUB applies once
     * per gesture (font + CFI restore are too heavy every frame).
     */
    function attachDocumentZoomInteraction(element) {
        if (!element || element.dataset.swZoomGuard === '1') return;
        element.dataset.swZoomGuard = '1';

        element.addEventListener('wheel', function (e) {
            if (!e.ctrlKey) return;
            e.preventDefault();
            if (state.rendition) {
                adjustEpubFontScale(e.deltaY > 0 ? -1 : 1);
            } else if (state.pdfDoc) {
                adjustPdfUserScale(e.deltaY > 0 ? -1 : 1);
            }
        }, { passive: false });

        var pinchStartDist = 0;
        var pinchStartPdf = 1;
        var pinchStartEpub = 1;
        var pinchRaf = 0;
        var pinchPendingFactor = 1;

        function flushPdfPinch() {
            pinchRaf = 0;
            if (!state.pdfDoc || pinchStartDist <= 0) return;
            var factor = clamp(pinchPendingFactor, 0.15, 6);
            setPdfUserScale(clamp(pinchStartPdf * factor, PDF_ZOOM_MIN, PDF_ZOOM_MAX));
        }

        element.addEventListener('touchstart', function (e) {
            if (e.touches.length === 2) {
                pinchStartDist = touchPairDistance(e.touches[0], e.touches[1]);
                pinchStartPdf = state.pdfUserScale;
                pinchStartEpub = state.epubFontScale;
                pinchPendingFactor = 1;
            }
        }, { passive: true });

        element.addEventListener('touchmove', function (e) {
            if (e.touches.length !== 2 || pinchStartDist <= 0) return;
            e.preventDefault();
            var d = touchPairDistance(e.touches[0], e.touches[1]);
            pinchPendingFactor = d / pinchStartDist;
            if (state.pdfDoc && !pinchRaf) {
                pinchRaf = requestAnimationFrame(flushPdfPinch);
            }
        }, { passive: false });

        function endPinchGesture() {
            if (pinchRaf) {
                cancelAnimationFrame(pinchRaf);
                pinchRaf = 0;
            }
            if (pinchStartDist <= 0) return;
            var factor = clamp(pinchPendingFactor, 0.15, 6);
            if (state.pdfDoc) {
                setPdfUserScale(clamp(pinchStartPdf * factor, PDF_ZOOM_MIN, PDF_ZOOM_MAX));
            } else if (state.rendition) {
                setEpubFontScale(clamp(pinchStartEpub * factor, EPUB_FONT_MIN, EPUB_FONT_MAX));
            }
            pinchStartDist = 0;
        }

        element.addEventListener('touchend', function (e) {
            if (e.touches.length < 2) endPinchGesture();
        }, { passive: true });

        element.addEventListener('touchcancel', function () {
            if (pinchRaf) {
                cancelAnimationFrame(pinchRaf);
                pinchRaf = 0;
            }
            pinchStartDist = 0;
        }, { passive: true });
    }

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
        var sessionId = state.epubSessionId;
        if (!state.epubHadFirstProgressPush) {
            state.epubHadFirstProgressPush = true;
            try {
                if (sessionId === state.epubSessionId && state.dotnetRef) {
                    state.dotnetRef.invokeMethodAsync('OnProgress', percent, null, cfi || null);
                }
            } catch (err) {
                console.warn('EPUB progress (initial) failed', err);
            }
        }
        if (state.pendingProgressTimer) clearTimeout(state.pendingProgressTimer);
        state.pendingProgressTimer = setTimeout(function () {
            try {
                if (sessionId !== state.epubSessionId || !state.dotnetRef) return;
                state.dotnetRef.invokeMethodAsync('OnProgress', percent, null, cfi || null);
            } catch (err) {
                console.warn('Progress push failed', err);
            }
        }, EPUB_PROGRESS_DEBOUNCE_MS);
    }

    var pdfProgressTimer = null;
    function schedulePdfProgressPush() {
        if (!state.pdfDotnetRef || !state.pdfPageCount) return;
        if (pdfProgressTimer) clearTimeout(pdfProgressTimer);
        pdfProgressTimer = setTimeout(function () {
            var raw = (state.pdfCurrentPage / state.pdfPageCount) * 100;
            var percent = Math.min(100, Math.max(0, Math.round(raw)));
            if (percent === 0 && state.pdfCurrentPage > 0 && state.pdfCurrentPage < state.pdfPageCount) {
                percent = 1;
            }
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

                // Fit entire page in the scroll viewport (min of width / height), then apply user zoom.
                var dpr = window.devicePixelRatio || 1;
                var padX = 32;
                var padY = 28;
                var availableWidth = Math.max(32, state.pdfContainer.clientWidth - padX);
                var availableHeight = Math.max(48, state.pdfContainer.clientHeight - padY);
                var unscaledViewport = page.getViewport({ scale: 1 });
                var scaleW = availableWidth / unscaledViewport.width;
                var scaleH = availableHeight / unscaledViewport.height;
                var fitScale = Math.max(0.06, Math.min(scaleW, scaleH));
                var viewport = page.getViewport({ scale: fitScale * state.pdfUserScale * dpr });

                canvas.width = viewport.width;
                canvas.height = viewport.height;
                canvas.style.width = (viewport.width / dpr) + 'px';
                canvas.style.height = (viewport.height / dpr) + 'px';

                var ctx = canvas.getContext('2d');
                await page.render({ canvasContext: ctx, viewport: viewport }).promise;
                pageWrapper.style.minHeight = '';
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
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    var pageNum = parseInt(entry.target.dataset.page, 10);
                    renderPdfPage(pageNum);
                    if (pageNum + 1 <= state.pdfPageCount) renderPdfPage(pageNum + 1);
                    if (pageNum - 1 >= 1) renderPdfPage(pageNum - 1);
                }
            });
            // Never derive current page from `entries` alone — it is only a delta batch.
            schedulePdfScrollSync();
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
        mountEpub: function (containerId, url, dotnetRef, resumeCfi) {
            var self = this;
            epubMountChain = epubMountChain.then(function () {
                return self._mountEpubAsync(containerId, url, dotnetRef, resumeCfi);
            });
            return epubMountChain;
        },

        _mountEpubAsync: async function (containerId, url, dotnetRef, resumeCfi) {
            await ensureEpubJsLoaded();
            this.disposeEpub();
            var sessionId = state.epubSessionId;

            var mountGate = createEpubMountGate();
            state.epubMountGate = mountGate;

            state.dotnetRef = dotnetRef;
            state.epubContainerId = containerId;
            state.epubLocationsReady = false;

            var host = document.getElementById(containerId);
            if (host) {
                host.innerHTML = '';
            }
            try {
            state.epub = window.ePub(url, { openAs: 'epub' });
            state.rendition = state.epub.renderTo(containerId, {
                width: '100%',
                height: '100%',
                manager: 'default',
                flow: 'paginated',
                spread: 'auto',
            });

            state.epubMountSeedDone = false;
            state.rendition.on('relocated', function (location) {
                if (sessionId !== state.epubSessionId) return;
                if (!state.epubMountSeedDone) return;
                var cfi = location && location.start ? location.start.cfi : null;
                var percent = computeEpubProgressPercent(state.epub, location);
                state.currentCfi = cfi;
                state.currentPercent = percent;
                scheduleProgressPush(percent, cfi);
            });

            await state.epub.ready;
            if (sessionId !== state.epubSessionId) return;

            if (state.rendition.started && typeof state.rendition.started.then === 'function') {
                await state.rendition.started;
            }
            if (sessionId !== state.epubSessionId) return;

            await state.rendition.display(resumeCfi || undefined);
            if (sessionId !== state.epubSessionId) return;

            await seedEpubProgressAfterMount(sessionId);
            state.epubMountSeedDone = true;

            // Never hold this gate until locations.generate() — that pass can take a long time
            // or stall on some EPUBs, which would freeze next/prev/zoom (they all await the gate).
            mountGate.resolve();

            // Character-offset index for finer "% complete"; runs in the background.
            (function () {
                var sid = sessionId;
                Promise.resolve().then(async function () {
                    try {
                        if (sid !== state.epubSessionId || !state.epub || !state.epub.locations) return;
                        await state.epub.locations.generate(1024);
                        if (sid !== state.epubSessionId) return;
                        state.epubLocationsReady = true;
                        var r = state.rendition;
                        if (r && typeof r.reportLocation === 'function') {
                            r.reportLocation();
                        }
                    } catch (err) {
                        if (sid === state.epubSessionId) {
                            console.warn('EPUB locations generation skipped', err);
                            state.epubLocationsReady = false;
                        }
                    }
                });
            })();

            await applyEpubTypographyAsync(sessionId);
            if (sessionId !== state.epubSessionId) return;

            attachDocumentZoomInteraction(host);
            applyReaderChromeViewport(true);
            } finally {
                mountGate.resolve();
            }
        },

        nextPage: function () {
            var sessionId = state.epubSessionId;
            epubRenditionChain = epubRenditionChain.then(async function () {
                await state.epubMountGate.promise;
                if (sessionId !== state.epubSessionId) return;
                var r = state.rendition;
                if (!(await whenEpubManagerReady(r, sessionId)) || typeof r.next !== 'function') return;
                var settled = waitForRelocated(r, 900);
                try {
                    await Promise.resolve(r.next());
                } catch (e) {
                    console.warn('EPUB next', e);
                }
                await settled;
            }).catch(function (e) {
                console.warn('EPUB next', e);
            });
        },
        prevPage: function () {
            var sessionId = state.epubSessionId;
            epubRenditionChain = epubRenditionChain.then(async function () {
                await state.epubMountGate.promise;
                if (sessionId !== state.epubSessionId) return;
                var r = state.rendition;
                if (!(await whenEpubManagerReady(r, sessionId)) || typeof r.prev !== 'function') return;
                var settled = waitForRelocated(r, 900);
                try {
                    await Promise.resolve(r.prev());
                } catch (e) {
                    console.warn('EPUB prev', e);
                }
                await settled;
            }).catch(function (e) {
                console.warn('EPUB prev', e);
            });
        },

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
            if (!cfi) return false;
            var sessionId = state.epubSessionId;
            epubRenditionChain = epubRenditionChain.then(async function () {
                await state.epubMountGate.promise;
                if (sessionId !== state.epubSessionId) return;
                var r = state.rendition;
                if (!(await whenEpubManagerReady(r, sessionId))) return;
                var settled = waitForRelocated(r, 900);
                try {
                    await Promise.resolve(r.display(cfi));
                } catch (err) {
                    console.warn('gotoEpub failed', err);
                }
                await settled;
            }).catch(function (err) {
                console.warn('gotoEpub failed', err);
            });
            return true;
        },

        disposeEpub: function () {
            if (state.epubMountGate && typeof state.epubMountGate.resolve === 'function') {
                state.epubMountGate.resolve();
            }
            state.epubMountGate = (function () {
                var g = createEpubMountGate();
                g.resolve();
                return g;
            })();
            state.epubSessionId += 1;
            state.epubHadFirstProgressPush = false;
            state.epubMountSeedDone = false;
            var containerIdToClear = state.epubContainerId;
            epubRenditionChain = Promise.resolve();

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
            state.epubContainerId = null;
            state.currentCfi = null;
            state.currentPercent = 0;
            state.epubLocationsReady = false;

            // Rendition leaves iframe(s) behind; stale DOM causes the next unpack to fail
            // (epub.js: this.resources undefined during replaceCss).
            if (containerIdToClear) {
                var host = document.getElementById(containerIdToClear);
                if (host) {
                    delete host.dataset.swZoomGuard;
                    host.innerHTML = '';
                }
            }
            state.epubFontScale = 1;
        },

        /**
         * Call when leaving the reader page (after disposeEpub + disposePdf) so the global
         * viewport meta is restored; do not call from individual disposes or in-book navigations
         * would flash the default viewport.
         */
        releaseReaderChromeViewport: function () {
            applyReaderChromeViewport(false);
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

            if (state.pdfScrollListener && container) {
                container.removeEventListener('scroll', state.pdfScrollListener);
            }
            state.pdfScrollListener = function () {
                schedulePdfScrollSync();
            };
            container.addEventListener('scroll', state.pdfScrollListener, { passive: true });

            if (resumePage && resumePage > 1 && resumePage <= state.pdfPageCount) {
                // Wait a tick so the placeholders have been laid out before scrollIntoView.
                setTimeout(function () {
                    var el = state.pdfPageElements.get(resumePage);
                    if (el) el.scrollIntoView({ block: 'start' });
                }, 0);
            }

            attachDocumentZoomInteraction(container);
            applyReaderChromeViewport(true);

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
            setTimeout(function () {
                schedulePdfScrollSync();
            }, 450);
            return true;
        },

        nextPdfPage: function () {
            return this.gotoPdfPage((state.pdfCurrentPage || 1) + 1);
        },

        prevPdfPage: function () {
            return this.gotoPdfPage((state.pdfCurrentPage || 1) - 1);
        },

        zoomIn: function () {
            var a = getActiveAdapter();
            if (a) a.zoomIn();
        },

        zoomOut: function () {
            var a = getActiveAdapter();
            if (a) a.zoomOut();
        },

        resetZoom: function () {
            var a = getActiveAdapter();
            if (a) a.resetZoom();
        },

        /**
         * @param {number} scale — PDF: user multiplier on fit-width (1 = default). EPUB: font scale (1 = 100%).
         */
        setScale: function (scale) {
            var a = getActiveAdapter();
            if (!a) return;
            var s = typeof scale === 'number' ? scale : parseFloat(scale);
            if (!isFinite(s) || s <= 0) return;
            a.setScale(s);
        },

        disposePdf: function () {
            if (pdfProgressTimer) { clearTimeout(pdfProgressTimer); pdfProgressTimer = null; }
            if (pdfScrollSyncTimer) {
                clearTimeout(pdfScrollSyncTimer);
                pdfScrollSyncTimer = null;
            }
            if (state.pdfContainer && state.pdfScrollListener) {
                state.pdfContainer.removeEventListener('scroll', state.pdfScrollListener);
            }
            state.pdfScrollListener = null;
            if (state.pdfPageObserver) {
                state.pdfPageObserver.disconnect();
                state.pdfPageObserver = null;
            }
            if (state.pdfDoc) {
                try { state.pdfDoc.destroy(); } catch (e) { }
                state.pdfDoc = null;
            }
            if (state.pdfContainer) {
                delete state.pdfContainer.dataset.swZoomGuard;
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
            state.pdfUserScale = 1;
        },
    };
})();
