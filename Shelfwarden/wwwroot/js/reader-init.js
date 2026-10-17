/**
 * Runs in <head> before paint so the ebook reader opens in the right mode with no
 * light-then-dark flash. The Bootswatch theme itself is applied by the shared theme-init.js
 * that the host page loads. Kept in sync by reader.js.
 */
(function () {
    var READER_DARK_KEY = 'shelfwarden.reader.darkMode';

    try {
        if (localStorage.getItem(READER_DARK_KEY) === '1') {
            document.documentElement.classList.add('reader-dark');
        }
    } catch (e) {
        /* private mode / blocked storage */
    }
})();
