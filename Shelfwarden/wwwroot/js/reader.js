// shelfwardenReader — Blazor interop for epub.js (EPUB) and pdf.js (PDF). Libraries live under
// wwwroot/lib. Only `.reader-document-content` scrolls/zooms; chrome is fixed in Blazor.

(function () {
    if (window.shelfwardenReader) return;

    // --- Constants -------------------------------------------------------------------------

    var PDF_ZOOM_MIN = 0.12;
    var PDF_ZOOM_MAX = 3;
    var PDF_ZOOM_STEP = 0.12;
    var EPUB_PROGRESS_DEBOUNCE_MS = 650;
    var EPUB_FONT_MIN = 0.75;
    var EPUB_FONT_MAX = 2.25;
    var EPUB_FONT_STEP = 0.1;
    var EPUB_LOCATION_PROBE_FRAMES = 16;
    var READER_DARK_KEY = 'shelfwarden.reader.darkMode';

    /**
     * Mobile two-finger pinch-to-zoom on the reader document. Disabled for now: it still
     * fights native scroll / overflow clipping / lazy pdf.js renders on small screens.
     * Trackpad ctrl+wheel zoom remains enabled. Set true to re-test after a proper rework.
     */
    var ENABLE_TOUCH_PINCH_ZOOM = false;

    /** Serialize EPUB opens — overlapping unpack() corrupts epub.js. */
    var epubMountChain = Promise.resolve();

    /** Serialize EPUB pagination, typography, and goto so manager state stays consistent. */
    var epubRenditionChain = Promise.resolve();

    function createEpubMountGate() {
        var gate = {};
        gate.promise = new Promise(function (resolve) {
            gate.resolve = resolve;
        });
        return gate;
    }

    // --- Mutable state --------------------------------------------------------------------

    var state = {
        epub: null,
        rendition: null,
        epubContainerId: null,
        epubSessionId: 0,
        epubLocationsReady: false,
        /** Open after first display+seed so next/prev/zoom never block on locations.generate(). */
        epubMountGate: createEpubMountGate(),
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
        /** @type {number|null} Page a bookmark asked to jump to before the PDF finished mounting. */
        pendingPdfGoto: null,
        /** @type {number} Multiplier applied on top of fit-to-width pdf.js viewport scale. */
        pdfUserScale: 1,
        /** @type {function(): void|null} */
        pdfScrollListener: null,
        /** @type {HTMLDivElement|null} Inner wrapper used for CSS-transform pinch preview. */
        pdfPagesWrapper: null,
        /** Tracks reader dark preference for the content hook (epub iframe). */
        epubReaderDarkMode: false,
        epubReaderThemeHookInstalled: false,
    };
    state.epubMountGate.resolve();

    /** Tracks epub iframe documents that already have swipe listeners attached. */
    var epubSwipeAttachedDocs = new WeakSet();

    var pdfResizeTimer = null;

    function clamp(n, lo, hi) {
        return Math.min(hi, Math.max(lo, n));
    }

    function readReaderDarkMode() {
        try {
            return localStorage.getItem(READER_DARK_KEY) === '1';
        } catch (e) {
            return false;
        }
    }

    function writeReaderDarkMode(on) {
        try {
            localStorage.setItem(READER_DARK_KEY, on ? '1' : '0');
        } catch (e) { /* private mode / blocked storage */ }
    }

    function applyReaderDarkChrome(on) {
        // The class lives on <html> (not .reader-shell) so it can be set pre-paint by theme-init.js
        // and Blazor re-renders of the shell never wipe it — this is what removes the mode flicker.
        document.documentElement.classList.toggle('reader-dark', !!on);
    }

    var EPUB_READER_THEME_KEY = 'shelfwarden-reader-theme';
    var EPUB_READER_THEME_CSS = {
        light: [
            'body { background: #ffffff !important; color: #1a1a1a !important; }',
            'p, li, span, div, h1, h2, h3, h4, h5, h6 { color: inherit !important; }',
            'a { color: inherit !important; }',
        ].join('\n'),
        dark: [
            'body { background: #1a1a1a !important; color: #e6e6e6 !important; }',
            'p, li, span, div, h1, h2, h3, h4, h5, h6 { color: #e6e6e6 !important; }',
            'a { color: #7eb8ff !important; }',
        ].join('\n'),
    };

    /** epub.js themes.select() stacks styles and cannot reliably revert to light — use addStylesheetCss instead. */
    function cleanupLegacyEpubThemeStyles(content) {
        if (!content || !content.document) return;
        var doc = content.document;
        var legacy = doc.getElementById('epubjs-inserted-css-shelfwarden-dark');
        if (legacy && legacy.parentNode) {
            legacy.parentNode.removeChild(legacy);
        }
        if (typeof content.removeClass === 'function') {
            content.removeClass('shelfwarden-dark');
        }
    }

    function applyEpubReaderThemeToContent(content, isDark) {
        if (!content || typeof content.addStylesheetCss !== 'function') return;
        cleanupLegacyEpubThemeStyles(content);
        content.addStylesheetCss(
            isDark ? EPUB_READER_THEME_CSS.dark : EPUB_READER_THEME_CSS.light,
            EPUB_READER_THEME_KEY);
    }

    function applyEpubReaderTheme(isDark) {
        state.epubReaderDarkMode = !!isDark;
        var r = state.rendition;
        if (!r || typeof r.getContents !== 'function') return;
        try {
            r.getContents().forEach(function (content) {
                applyEpubReaderThemeToContent(content, isDark);
            });
        } catch (err) {
            console.warn('EPUB reader theme', err);
        }
    }

    function installEpubReaderThemeHook(sessionId) {
        var r = state.rendition;
        if (!r || !r.hooks || !r.hooks.content || state.epubReaderThemeHookInstalled) return;
        state.epubReaderThemeHookInstalled = true;
        r.hooks.content.register(function (contents) {
            if (sessionId !== state.epubSessionId) return;
            applyEpubReaderThemeToContent(contents, state.epubReaderDarkMode);
        });
    }

    function applyEpubDarkTheme(isDark) {
        applyEpubReaderTheme(isDark);
    }

    function setReaderDarkMode(on) {
        writeReaderDarkMode(on);
        applyReaderDarkChrome(on);
        applyEpubDarkTheme(on);
    }

    function delay(ms) {
        return new Promise(function (resolve) {
            setTimeout(resolve, ms);
        });
    }

    function nextFrame() {
        return new Promise(function (resolve) {
            requestAnimationFrame(resolve);
        });
    }

    function reopenEpubMountGate() {
        if (state.epubMountGate && typeof state.epubMountGate.resolve === 'function') {
            state.epubMountGate.resolve();
        }
        var g = createEpubMountGate();
        g.resolve();
        state.epubMountGate = g;
    }

    /**
     * @param {number} sessionId
     * @param {string} label — console label on failure
     * @param {function(): Promise<void>} work
     */
    function chainEpubRendition(sessionId, label, work) {
        epubRenditionChain = epubRenditionChain.then(async function () {
            await state.epubMountGate.promise;
            if (sessionId !== state.epubSessionId) return;
            await work();
        }).catch(function (e) {
            console.warn(label, e);
        });
    }

    // --- Zoom adapters & EPUB typography -------------------------------------------------

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

    // --- EPUB progress --------------------------------------------------------------------

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
            await delay(0);
            var loc = null;
            for (var attempt = 0; attempt < EPUB_LOCATION_PROBE_FRAMES; attempt++) {
                await nextFrame();
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

    function installEpubRelocatedHandler(sessionId) {
        state.epubMountSeedDone = false;
        state.rendition.on('relocated', function (location) {
            if (sessionId !== state.epubSessionId || !state.epubMountSeedDone) return;
            var cfi = location && location.start ? location.start.cfi : null;
            var percent = computeEpubProgressPercent(state.epub, location);
            state.currentCfi = cfi;
            state.currentPercent = percent;
            scheduleProgressPush(percent, cfi);
        });
    }

    function startBackgroundEpubLocationIndex(sessionId) {
        Promise.resolve().then(async function () {
            try {
                if (sessionId !== state.epubSessionId || !state.epub || !state.epub.locations) return;
                await state.epub.locations.generate(1024);
                if (sessionId !== state.epubSessionId) return;
                state.epubLocationsReady = true;
                var r = state.rendition;
                if (r && typeof r.reportLocation === 'function') {
                    r.reportLocation();
                }
            } catch (err) {
                if (sessionId === state.epubSessionId) {
                    console.warn('EPUB locations generation skipped', err);
                    state.epubLocationsReady = false;
                }
            }
        });
    }

    // --- PDF ------------------------------------------------------------------------------

    function invalidateAllPdfRenders() {
        state.pdfRenderedPages.clear();
        state.pdfPageElements.forEach(function (wrapper) {
            var h = wrapper.offsetHeight;
            if (h > 0) {
                wrapper.style.minHeight = h + 'px';
            }
            // Clear the canvas so every page is in a consistent blank state at the new
            // scale. Pages re-render via the offscreen-swap path: visible pages are
            // queued immediately by rerenderVisiblePdfPages (so their blank period is
            // minimal), while off-screen pages re-render lazily as they scroll into view.
            // This prevents the "mixed sizes" artifact where some pages show old-scale
            // canvases while others already show new-scale canvases.
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
     * Ctrl+wheel (trackpad pinch) + two-finger pinch zoom for PDF and EPUB.
     *
     * PDF: no canvas operations happen mid-gesture (would blank the page). The new scale
     * is committed and pages re-rendered in one pass when the fingers lift.
     *
     * EPUB: font resize is applied once per gesture — it's too heavy to do every frame.
     *
     * Navigation-during-zoom is prevented by two mechanisms:
     *   1. preventDefault on touchstart (2 fingers) aborts any in-progress browser pan.
     *   2. A short cooldown after gesture end blocks the "trailing" single-finger scroll
     *      that would otherwise fire while the second finger is still lifting.
     *
     * Pages-disappearing-during-pinch was caused by CSS transform: scale() on an element
     * inside overflow:auto — the scroll container clips the transformed content. We no
     * longer apply any CSS transform mid-gesture; the scale is only committed at the end.
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

        if (ENABLE_TOUCH_PINCH_ZOOM) {
            var pinchStartDist = 0;
            var pinchStartPdf = 1;
            var pinchStartEpub = 1;
            var pinchPendingFactor = 1;

            // After a pinch ends, the finger that lifts last can still produce touchmove
            // events that scroll the PDF to a different page. Block those for a short window.
            var pinchActive = false;
            var pinchCooldownTimer = null;

            function beginPinchCooldown() {
                pinchActive = true;
                if (pinchCooldownTimer) clearTimeout(pinchCooldownTimer);
                pinchCooldownTimer = setTimeout(function () {
                    pinchActive = false;
                }, 380);
            }

            element.addEventListener('touchstart', function (e) {
                if (e.touches.length === 2) {
                    // preventDefault on touchstart (non-passive) cancels any browser pan/scroll
                    // that began when the first finger landed before the second arrived.
                    e.preventDefault();
                    // Also cancel the cooldown — the user put a second finger down again.
                    pinchActive = false;
                    if (pinchCooldownTimer) { clearTimeout(pinchCooldownTimer); pinchCooldownTimer = null; }
                    pinchStartDist = touchPairDistance(e.touches[0], e.touches[1]);
                    pinchStartPdf = state.pdfUserScale;
                    pinchStartEpub = state.epubFontScale;
                    pinchPendingFactor = 1;
                }
            }, { passive: false });

            element.addEventListener('touchmove', function (e) {
                if (pinchActive && e.touches.length < 2) {
                    // Block the trailing single-finger scroll during the cooldown window.
                    e.preventDefault();
                    return;
                }
                if (e.touches.length !== 2 || pinchStartDist <= 0) return;
                e.preventDefault();
                var d = touchPairDistance(e.touches[0], e.touches[1]);
                pinchPendingFactor = d / pinchStartDist;
            }, { passive: false });

            function endPinchGesture() {
                if (pinchStartDist <= 0) return;
                beginPinchCooldown();
                // Dampen the raw distance ratio so a wide finger spread doesn't jump to an
                // extreme scale in one gesture. Math.pow(x, 0.65) compresses the magnitude
                // while preserving direction — e.g. a 3× spread → ~2× zoom.
                var rawFactor = clamp(pinchPendingFactor, 0.15, 6);
                var factor = rawFactor >= 1
                    ? Math.pow(rawFactor, 0.65)
                    : 1 / Math.pow(1 / rawFactor, 0.65);
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
                if (pinchStartDist > 0) beginPinchCooldown();
                pinchStartDist = 0;
            }, { passive: true });
        }
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
     * Render one PDF page. Pages render lazily as they scroll into view.
     * Renders into an offscreen canvas first, then swaps it into the DOM in one
     * synchronous step. This means the page goes from "placeholder / previous render"
     * directly to fully-painted content with no partially-drawn intermediate frame.
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

                // Render into an offscreen canvas first so the visible DOM canvas is never blank.
                var offscreen = document.createElement('canvas');
                offscreen.width = viewport.width;
                offscreen.height = viewport.height;
                await page.render({ canvasContext: offscreen.getContext('2d'), viewport: viewport }).promise;

                // Atomic swap: insert the fully-rendered canvas before removing the old one.
                // The old pixels stay on-screen until the new frame is composited — no blank flash.
                offscreen.style.display = 'block';
                offscreen.style.width = (viewport.width / dpr) + 'px';
                offscreen.style.height = (viewport.height / dpr) + 'px';
                var oldCanvas = pageWrapper.querySelector('canvas');
                if (oldCanvas) {
                    pageWrapper.insertBefore(offscreen, oldCanvas);
                    pageWrapper.removeChild(oldCanvas);
                } else {
                    pageWrapper.appendChild(offscreen);
                }
                pageWrapper.style.minHeight = '';
            } catch (err) {
                console.warn('Failed to render PDF page ' + pageNum, err);
                state.pdfRenderedPages.delete(pageNum);
            }
        });
    }

    /**
     * Give every not-yet-rendered page an estimated height so the scroll container has a stable
     * total height *before* any page paints. Without this, pages above a bookmark/resume target
     * are collapsed to ~0px while loading, so scrolling to page N lands in the wrong place and the
     * position jumps around as earlier pages render in. Estimated from page 1's aspect ratio.
     */
    async function applyPdfPlaceholderHeights() {
        if (!state.pdfDoc || !state.pdfContainer) return;
        try {
            var firstPage = await state.pdfDoc.getPage(1);
            var vp = firstPage.getViewport({ scale: 1 });
            var availableWidth = Math.max(32, state.pdfContainer.clientWidth - 32);
            var availableHeight = Math.max(48, state.pdfContainer.clientHeight - 28);
            var fitScale = Math.max(0.06, Math.min(availableWidth / vp.width, availableHeight / vp.height));
            var estimated = Math.round(vp.height * fitScale * state.pdfUserScale);
            if (estimated <= 0) return;
            state.pdfPageElements.forEach(function (el) {
                var pageNum = parseInt(el.dataset.page, 10);
                if (!state.pdfRenderedPages.has(pageNum)) {
                    el.style.minHeight = estimated + 'px';
                }
            });
        } catch (err) {
            // Non-fatal: navigation still works, it just may need the delayed re-align below.
            console.warn('applyPdfPlaceholderHeights failed', err);
        }
    }

    /**
     * Scroll a page into view robustly, even while the document is still loading. Renders the target
     * (plus neighbours) and re-aligns a few times as nearby pages finish painting and shift offsets,
     * which is what previously made bookmark jumps miss while the book was loading.
     */
    function scrollToPdfPage(pageNumber, smooth) {
        if (!pageNumber) return false;
        // Called before the document finished mounting: remember it and apply once ready.
        if (!state.pdfPageElements.size || !state.pdfDoc) {
            state.pendingPdfGoto = pageNumber;
            return false;
        }
        var clamped = Math.max(1, Math.min(state.pdfPageCount, pageNumber));
        var el = state.pdfPageElements.get(clamped);
        if (!el) return false;

        renderPdfPage(clamped);
        if (clamped + 1 <= state.pdfPageCount) renderPdfPage(clamped + 1);
        if (clamped - 1 >= 1) renderPdfPage(clamped - 1);

        el.scrollIntoView({ block: 'start', behavior: smooth ? 'smooth' : 'auto' });
        state.pdfCurrentPage = clamped;

        [120, 400, 800].forEach(function (delay) {
            setTimeout(function () {
                var target = state.pdfPageElements.get(clamped);
                if (target) target.scrollIntoView({ block: 'start' });
                schedulePdfScrollSync();
            }, delay);
        });
        return true;
    }

    function buildPdfPagePlaceholders() {
        state.pdfContainer.innerHTML = '';
        state.pdfPageElements.clear();
        state.pdfRenderedPages.clear();

        // Inner wrapper: CSS transform is applied here during pinch for a smooth preview
        // without touching canvas content. The outer container keeps its scroll position.
        var pagesWrapper = document.createElement('div');
        pagesWrapper.className = 'pdf-pages-wrapper';
        state.pdfContainer.appendChild(pagesWrapper);
        state.pdfPagesWrapper = pagesWrapper;

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

            pagesWrapper.appendChild(pageWrapper);
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

    // --- EPUB swipe navigation -----------------------------------------------------------

    /**
     * Attach touchstart/touchend swipe-detection to an epub.js view's iframe document.
     * Called on every `rendered` event so new spreads/sections are covered after navigation.
     * Uses a WeakSet so duplicate attachment is a no-op even if epub.js fires `rendered`
     * multiple times for the same view.
     */
    function attachSwipeToEpubView(view, sessionId) {
        if (!view) return;
        var doc = null;
        try {
            doc = view.document || (view.iframe && view.iframe.contentDocument);
        } catch (e) { return; }
        if (!doc || epubSwipeAttachedDocs.has(doc)) return;
        epubSwipeAttachedDocs.add(doc);

        var startX = 0, startY = 0, nTouches = 0;

        doc.addEventListener('touchstart', function (e) {
            nTouches = e.touches.length;
            if (e.touches.length === 1) {
                startX = e.touches[0].clientX;
                startY = e.touches[0].clientY;
            }
        }, { passive: true });

        doc.addEventListener('touchend', function (e) {
            if (sessionId !== state.epubSessionId) return;
            if (nTouches !== 1 || e.changedTouches.length < 1) return;
            var dx = e.changedTouches[0].clientX - startX;
            var dy = e.changedTouches[0].clientY - startY;
            // Require a clear horizontal swipe: at least 40 px and not more diagonal than 3:4.
            if (Math.abs(dx) < 40 || Math.abs(dy) > Math.abs(dx) * 0.75) return;
            if (dx < 0) {
                window.shelfwardenReader.nextPage();
            } else {
                window.shelfwardenReader.prevPage();
            }
        }, { passive: true });
    }

    /**
     * Subscribe to the epub.js `rendered` event so every new spread/section gets swipe
     * listeners, and also attach to any views that are already rendered at mount time.
     */
    function installEpubSwipeHandlers(sessionId) {
        var r = state.rendition;
        if (!r) return;
        r.on('rendered', function (section, view) {
            if (sessionId !== state.epubSessionId) return;
            attachSwipeToEpubView(view, sessionId);
        });
        // Cover views that are already displayed before the listener was registered.
        try {
            var initial = typeof r.getContents === 'function' ? r.getContents() : [];
            if (Array.isArray(initial)) {
                initial.forEach(function (c) { attachSwipeToEpubView(c, sessionId); });
            }
        } catch (e) { /* ignore — view API varies across epub.js versions */ }
    }

    // --- Public API (Blazor invokes these) ------------------------------------------------

    window.shelfwardenReader = {
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

                installEpubRelocatedHandler(sessionId);

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

                mountGate.resolve();
                startBackgroundEpubLocationIndex(sessionId);

                await applyEpubTypographyAsync(sessionId);
                if (sessionId !== state.epubSessionId) return;

                attachDocumentZoomInteraction(host);
                installEpubSwipeHandlers(sessionId);
                installEpubReaderThemeHook(sessionId);
                applyReaderChromeViewport(true);
                setReaderDarkMode(readReaderDarkMode());
            } finally {
                mountGate.resolve();
            }
        },

        nextPage: function () {
            var sessionId = state.epubSessionId;
            chainEpubRendition(sessionId, 'EPUB next', async function () {
                var r = state.rendition;
                if (!(await whenEpubManagerReady(r, sessionId)) || typeof r.next !== 'function') return;
                var settled = waitForRelocated(r, 900);
                try {
                    await Promise.resolve(r.next());
                } catch (e) {
                    console.warn('EPUB next', e);
                }
                await settled;
            });
        },
        prevPage: function () {
            var sessionId = state.epubSessionId;
            chainEpubRendition(sessionId, 'EPUB prev', async function () {
                var r = state.rendition;
                if (!(await whenEpubManagerReady(r, sessionId)) || typeof r.prev !== 'function') return;
                var settled = waitForRelocated(r, 900);
                try {
                    await Promise.resolve(r.prev());
                } catch (e) {
                    console.warn('EPUB prev', e);
                }
                await settled;
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
            chainEpubRendition(sessionId, 'gotoEpub failed', async function () {
                var r = state.rendition;
                if (!(await whenEpubManagerReady(r, sessionId))) return;
                var settled = waitForRelocated(r, 900);
                try {
                    await Promise.resolve(r.display(cfi));
                } catch (err) {
                    console.warn('gotoEpub failed', err);
                }
                await settled;
            });
            return true;
        },

        /** Jump to the start of the book (first linear spine item). */
        firstEpubPage: function () {
            var sessionId = state.epubSessionId;
            chainEpubRendition(sessionId, 'EPUB first page', async function () {
                var r = state.rendition;
                var book = state.epub;
                if (!(await whenEpubManagerReady(r, sessionId)) || !book || !book.spine) return;
                var href = null;
                var spine = book.spine;
                if (typeof spine.first === 'function') {
                    var sec = spine.first();
                    href = sec && sec.href ? sec.href : null;
                }
                if (!href && spine.get) {
                    var s0 = spine.get(0);
                    href = s0 && s0.href ? s0.href : null;
                }
                if (!href) return;
                var settled = waitForRelocated(r, 900);
                try {
                    await Promise.resolve(r.display(href));
                } catch (e) {
                    console.warn('EPUB first page', e);
                }
                await settled;
            });
        },

        disposeEpub: function () {
            reopenEpubMountGate();
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
            state.epubReaderThemeHookInstalled = false;

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
            await applyPdfPlaceholderHeights();
            setupPdfObserver();

            if (state.pdfScrollListener && container) {
                container.removeEventListener('scroll', state.pdfScrollListener);
            }
            state.pdfScrollListener = function () {
                schedulePdfScrollSync();
            };
            container.addEventListener('scroll', state.pdfScrollListener, { passive: true });

            // A bookmark clicked before the mount finished is queued in pendingPdfGoto; it takes
            // precedence over the resume position so the jump the user asked for still happens.
            var initialPage = state.pendingPdfGoto || resumePage;
            state.pendingPdfGoto = null;
            if (initialPage && initialPage > 1 && initialPage <= state.pdfPageCount) {
                setTimeout(function () {
                    scrollToPdfPage(initialPage, false);
                }, 0);
            }

            attachDocumentZoomInteraction(container);
            applyReaderChromeViewport(true);
            applyReaderDarkChrome(readReaderDarkMode());

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
            return scrollToPdfPage(pageNumber, true);
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

        getDarkMode: function () {
            return readReaderDarkMode();
        },

        setDarkMode: function (on) {
            setReaderDarkMode(!!on);
        },

        /** Flip dark mode without a Blazor round-trip — the button's icon/style react via CSS off `.reader-dark`. */
        toggleDarkMode: function () {
            setReaderDarkMode(!readReaderDarkMode());
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
            state.pdfPagesWrapper = null;
            state.pdfPageCount = 0;
            state.pdfCurrentPage = 1;
            state.pdfPageElements.clear();
            state.pdfRenderedPages.clear();
            state.pdfDotnetRef = null;
            state.pdfRenderQueue = Promise.resolve();
            state.pdfUserScale = 1;
            state.pendingPdfGoto = null;
        },
    };
})();
