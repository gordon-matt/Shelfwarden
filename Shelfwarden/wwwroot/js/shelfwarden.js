// Small surface area for Blazor JS interop. Each property on `window.shelfwarden`
// is a single-purpose helper; keep things flat so the C# call sites read naturally.
window.shelfwarden = {
    _observers: new Map(),
    /**
     * Live-swap the active Bootswatch stylesheet without a full page reload. Looks up the
     * <link id="theme-stylesheet"> element in App.razor and points it at a new URL.
     * @param {string} href Path under wwwroot to the new theme's bootstrap.min.css.
     */
    setThemeHref: function (href) {
        var link = document.getElementById('theme-stylesheet');
        if (link) {
            link.setAttribute('href', href);
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
    }
};
