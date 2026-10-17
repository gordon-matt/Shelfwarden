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

    public static readonly IReadOnlyList<string> Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".Select(c => c.ToString()).ToList();

    /// <summary>
    /// Default collection name used when a shelf is configured to file newly-imported books
    /// into a collection but the administrator hasn't supplied a custom name.
    /// </summary>
    public const string DefaultNewBooksCollectionName = "To Review";

    /// <summary>
    /// URL/route discriminators used to scope card banner files (and the <c>/card-banners</c>
    /// controller routes) per entity kind. Centralising the strings here keeps the file naming
    /// scheme in lock-step between the storage provider, the helper, and the streaming controller.
    /// </summary>
    public static class CardBannerKinds
    {
        public const string Shelves = "shelves";
        public const string Collections = "collections";
        public const string ReadingLists = "reading-lists";
    }

    public static class HangfireQueues
    {
        public const string Critical = "critical";
        public const string Default = "default";
        public const string Scan = "scan";
        public const string Tts = "tts";
    }

    public static class Schemas
    {
        public const string App = "app";
    }

    /// <summary>
    /// Well-known keys persisted in the <c>ServerSettings</c> table by
    /// <c>IServerSettingsService</c>. Keep the strings stable — they are the source of truth
    /// once written to the database.
    /// </summary>
    public static class ServerSettingKeys
    {
        public const string OpdsEnabled = "opds.enabled";
        public const string SetupComplete = "setup.complete";
        public const string Theme = "ui.theme";
    }
}