// Small surface area for Blazor JS interop. Each property on `window.shelfwarden`
// is a single-purpose helper; keep things flat so the C# call sites read naturally.
window.shelfwarden = {
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
    }
};
