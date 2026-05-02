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
