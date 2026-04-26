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
        // jszip is required by epub.js — load it first.
        await loadScript('https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js');
        await loadScript('https://cdnjs.cloudflare.com/ajax/libs/epub.js/0.3.93/epub.min.js');
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
            frame.src = url + '#toolbar=1';
        },
    };
})();
