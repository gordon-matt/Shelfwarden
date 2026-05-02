// Small surface area for Blazor JS interop. Each property on `window.shelfwarden`
// is a single-purpose helper; keep things flat so the C# call sites read naturally.
window.shelfwarden = {
    _observers: new Map(),
    _themeModalDismissRegistered: new Set(),
    /**
     * Live-swap the active Bootswatch stylesheet without a full page reload.
     * @param {string} href Path under wwwroot to the new theme's bootstrap.min.css.
     * @param {string} [dataBsTheme] Optional "dark" | "light" — must match Bootswatch dark vs light
     *   themes so Bootstrap + Bootswatch CSS variable overrides (e.g. emphasis + sidebar) apply.
     */
    setThemeHref: function (href, dataBsTheme) {
        var link = document.getElementById('theme-stylesheet');
        if (link) {
            link.setAttribute('href', href);
        }
        if (dataBsTheme === 'dark' || dataBsTheme === 'light') {
            document.documentElement.setAttribute('data-bs-theme', dataBsTheme);
        }
    },

    /** One-time: wires Bootstrap's hidden.bs.modal so Blazor can revert an uncommitted theme preview. */
    registerThemeModalDismiss: function (modalId, dotNetRef) {
        if (window.shelfwarden._themeModalDismissRegistered.has(modalId)) {
            return;
        }
        var el = document.getElementById(modalId);
        if (!el) {
            return;
        }
        el.addEventListener('hidden.bs.modal', function () {
            dotNetRef.invokeMethodAsync('OnThemeModalDismissed');
        });
        window.shelfwarden._themeModalDismissRegistered.add(modalId);
    },

    hideModal: function (modalId) {
        var el = document.getElementById(modalId);
        if (!el || typeof bootstrap === 'undefined') {
            return;
        }
        var inst = bootstrap.Modal.getInstance(el);
        if (inst) {
            inst.hide();
        }
    },

    observeInfiniteScroll: function (key, element, dotNetRef, callbackMethodName) {
        if (!key || !element || !dotNetRef || !callbackMethodName) {
            return;
        }

        this.disconnectInfiniteScroll(key);

        var observer = new IntersectionObserver(function (entries) {
            for (var i = 0; i < entries.length; i++) {
                if (entries[i].isIntersecting) {
                    dotNetRef.invokeMethodAsync(callbackMethodName);
                    break;
                }
            }
        }, {
            root: null,
            threshold: 0.1
        });

        observer.observe(element);
        this._observers.set(key, observer);
    },

    disconnectInfiniteScroll: function (key) {
        var observer = this._observers.get(key);
        if (observer) {
            observer.disconnect();
            this._observers.delete(key);
        }
    },

    /** Native confirm — Blazor uses this so message escaping stays on the .NET side */
    confirmDialog: function (message) {
        return window.confirm(message);
    }
};

// Tiny audio helper for the voice-picker modal. Centralised so we only ever have to wire
// JS interop in one place — the modal hands us its own <audio> ElementReference and a URL,
// and we drive it from there.

window.shelfwardenAudio = {
    play: function (element, url) {
        if (!element) return;
        try {
            element.pause();
        } catch (_) { /* element may not have started yet */ }
        element.src = url;
        element.currentTime = 0;
        return element.play();
    },
    stop: function (element) {
        if (!element) return;
        try {
            element.pause();
            element.removeAttribute('src');
            element.load();
        } catch (_) { /* swallow */ }
    }
};
