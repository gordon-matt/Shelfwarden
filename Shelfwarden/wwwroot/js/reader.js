// Reader-specific JS interop. EPUB rendering is delegated to epub.js (loaded from a CDN
// the first time `mountEpub` is called); PDF rendering is just a thin wrapper around an
// <iframe> that points at pdf.js's bundled viewer. Keeping the surface area small so the
// Blazor side is the source of truth for navigation / progress save scheduling.

(function () {
    if (window.shelfwardenReader) return;

    var state = {
        epub: null,        // VersionEPub.js Book instance
        rendition: null,   // VersionEPub.js Rendition instance
        dotnetRef: null,   // DotNetObjectReference for progress callbacks
        scriptLoaded: false,
        pendingProgressTimer: null,
        currentCfi: null,        // Last CFI emitted by the EPUB rendition; used by bookmark create.
        currentPercent: 0,       // Last percent for the same.
        pdfFrameId: null,        // The iframe element id we mounted into so we can goto pages later.
        pdfBaseUrl: null,        // The /files/{id} URL for the active PDF (used for re-navigation).
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
         * Point an <iframe> at the streamed PDF URL. Modern Chromium/Firefox/Safari ship a
         * native PDF viewer so we don't need to ship pdf.js ourselves for v1. Pagination /
         * automatic progress tracking can come in a follow-up using pdf.js directly — for
         * now the user can manually mark progress via the bottom bar.
         */
        mountPdf: function (iframeId, url) {
            var frame = document.getElementById(iframeId);
            if (!frame) return;
            // Append #toolbar=1 — Chromium honours this to keep the toolbar visible. Other
            // browsers ignore unknown PDF fragment options.
            state.pdfFrameId = iframeId;
            state.pdfBaseUrl = url;
            frame.src = url + '#toolbar=1';
        },

        /**
         * Re-navigate the iframe to a specific PDF page. Browsers honour the standard
         * #page=N fragment for built-in PDF viewers (Chromium / Firefox / Safari all do).
         * Resetting `src` is required because changing only the hash on the same URL is a
         * no-op for cross-document navigation in some browsers.
         */
        gotoPdfPage: function (pageNumber) {
            if (!state.pdfFrameId || !state.pdfBaseUrl) return false;
            var frame = document.getElementById(state.pdfFrameId);
            if (!frame) return false;
            frame.src = state.pdfBaseUrl + '#page=' + (pageNumber || 1) + '&toolbar=1';
            return true;
        },
    };
})();
