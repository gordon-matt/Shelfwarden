/**
 * Runs in <head> before paint so the stored theme applies without a flash.
 * Keep ALLOWED + DARK in sync with Constants.BootswatchThemes in Shelfwarden.Core.
 */
(function () {
    var KEY = 'shelfwarden.theme';
    var ALLOWED = [
        'brite', 'cerulean', 'cosmo', 'cyborg', 'darkly', 'flatly', 'journal', 'litera', 'lumen',
        'lux', 'materia', 'minty', 'morph', 'pulse', 'quartz', 'sandstone', 'simplex', 'sketchy',
        'slate', 'solar', 'spacelab', 'superhero', 'united', 'vapor', 'yeti', 'zephyr'
    ];
    var DARK = { cyborg: 1, darkly: 1, slate: 1, solar: 1, superhero: 1, vapor: 1 };

    // Reader-only dark mode. Applied to <html> here — before first paint — so the ebook reader
    // opens in the correct mode with no light-then-dark flash. Kept in sync by reader.js.
    var READER_DARK_KEY = 'shelfwarden.reader.darkMode';

    try {
        if (localStorage.getItem(READER_DARK_KEY) === '1') {
            document.documentElement.classList.add('reader-dark');
        }
    } catch (e) {
        /* private mode / blocked storage */
    }

    try {
        var raw = localStorage.getItem(KEY);
        if (!raw) return;
        var t = String(raw).toLowerCase();
        if (ALLOWED.indexOf(t) < 0) return;

        var link = document.getElementById('theme-stylesheet');
        if (link) {
            link.setAttribute('href', 'lib/bootswatch/dist/' + t + '/bootstrap.min.css');
        }
        document.documentElement.setAttribute(
            'data-bs-theme',
            DARK[t] ? 'dark' : 'light');
    } catch (e) {
        /* private mode / blocked storage */
    }
})();
