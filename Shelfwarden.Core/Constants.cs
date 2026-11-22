namespace Shelfwarden;

public static class Constants
{
    /// <summary>Browser <c>localStorage</c> key for the per-user Bootswatch theme (client-only).</summary>
    public const string ClientThemeLocalStorageKey = "shelfwarden.theme";

    /// <summary>Browser <c>localStorage</c> key for the ebook reader dark-mode preference (client-only).</summary>
    public const string ReaderDarkModeLocalStorageKey = "shelfwarden.reader.darkMode";

    /// <summary>
    /// Reading-progress percentage at which a book is considered "finished". Used by the
    /// dashboard's Finished count, the "Continue Reading" filter, and the Mark as Read action
    /// so the UI and services can't drift apart.
    /// </summary>
    public const double FinishedThresholdPercent = 99d;

    /// <summary>
    /// Sentinel UserId stored on records that are global / system-wide rather than owned
    /// by a specific user (e.g. a built-in collection visible to everyone).
    /// </summary>
    public const string GlobalUserId = "_global";

    /// <summary>
    /// Letters used by the A–Z rail. A title or sort title that starts with none of these is filed under #.
    /// </summary>
    public static readonly IReadOnlyList<string> Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".Select(c => c.ToString()).ToList();

    /// <summary>
    /// Default collection name used when a shelf is configured to file newly-imported books
    /// into a collection but the administrator hasn't supplied a custom name.
    /// </summary>
    public const string DefaultNewBooksCollectionName = "To Review";

    /// <summary>
    /// Route and file-name segments for shelf, collection, and reading-list banners.
    /// Storage, the banner helper, and the streaming controller all use these same strings.
    /// </summary>
    public static class CardBannerKinds
    {
        /// <summary>Route and file-name segment for a shelf banner.</summary>
        public const string Shelves = "shelves";

        /// <summary>Route and file-name segment for a collection banner.</summary>
        public const string Collections = "collections";

        /// <summary>Route and file-name segment for a reading-list banner.</summary>
        public const string ReadingLists = "reading-lists";
    }

    /// <summary>Hangfire queue names. Scans use <see cref="Scan"/> and audiobook generation uses <see cref="Tts"/>.</summary>
    public static class HangfireQueues
    {
        /// <summary>Listened to by the main server, after <see cref="Default"/>.</summary>
        public const string Critical = "critical";

        /// <summary>Listened to by the main server, ahead of <see cref="Critical"/>.</summary>
        public const string Default = "default";

        /// <summary>The scan server's only queue. That server runs one job at a time.</summary>
        public const string Scan = "scan";

        /// <summary>The audiobook server's only queue. That server runs one job at a time.</summary>
        public const string Tts = "tts";
    }

    /// <summary>Database schema names. SQLite ignores them.</summary>
    public static class Schemas
    {
        /// <summary>Schema that holds the application tables.</summary>
        public const string App = "app";
    }

    /// <summary>
    /// Well-known keys persisted in the <c>ServerSettings</c> table by
    /// <c>IServerSettingsService</c>. Keep the strings stable — they are the source of truth
    /// once written to the database.
    /// </summary>
    public static class ServerSettingKeys
    {
        /// <summary>Whether the OPDS catalogue is served. Missing or unreadable means off.</summary>
        public const string OpdsEnabled = "opds.enabled";

        /// <summary>Whether the first-run wizard has been finished.</summary>
        public const string SetupComplete = "setup.complete";

        /// <summary>Stored Bootswatch theme name.</summary>
        public const string Theme = "ui.theme";
    }
}