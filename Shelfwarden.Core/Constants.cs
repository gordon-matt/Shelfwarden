namespace Shelfwarden;

public static class Constants
{
    /// <summary>Browser <c>localStorage</c> key for the per-user Bootswatch theme (client-only).</summary>
    public const string ClientThemeLocalStorageKey = "shelfwarden.theme";

    /// <summary>
    /// Synthetic user id used by the "None" authentication mode (desktop / kiosk scenarios).
    /// </summary>
    public const string DefaultUserId = "_default";

    /// <summary>
    /// Display name used for the synthetic user when no real auth provider is active.
    /// </summary>
    public const string DefaultUserName = "Default User";

    /// <summary>
    /// Reading-progress percentage at which a book is considered "finished". Used by the
    /// dashboard's Finished count, the "Continue Reading" filter, and the Mark as Read action
    /// so the UI and services can't drift apart.
    /// </summary>
    public const double FinishedThresholdPercent = 95d;

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

    public static class AuthProviders
    {
        public const string Identity = "Identity";
        public const string Keycloak = "Keycloak";
        public const string None = "None";
    }

    /// <summary>
    /// Bootswatch themes shipped via LibMan to <c>wwwroot/lib/bootswatch/dist/&lt;theme&gt;/bootstrap.min.css</c>.
    /// Keep this list in lock-step with the <c>files</c> entries in <c>libman.json</c> — themes
    /// listed here but not restored produce 404s when selected.
    /// </summary>
    public static class BootswatchThemes
    {
        public static readonly IReadOnlyList<string> All =
        [
            "brite", "cerulean", "cosmo", "cyborg", "darkly", "flatly", "journal", "litera", "lumen",
            "lux", "materia", "minty", "morph", "pulse", "quartz", "sandstone", "simplex", "sketchy",
            "slate", "solar", "spacelab", "superhero", "united", "vapor", "yeti", "zephyr"
        ];

        /// <summary>
        /// Themes with a dark chrome by default. Bootswatch still keys off <c>data-bs-theme</c> to
        /// apply the <c>[data-bs-theme=dark]</c> variable overrides; without it, <c>--bs-emphasis-color</c>
        /// and <c>--bs-tertiary-bg</c> can stay "light theme" values (e.g. black headings on a dark body,
        /// light gray sidebar on a dark page). These names match <see cref="All"/> (libman + theme picker).
        /// </summary>
        public static readonly IReadOnlySet<string> DarkColorScheme =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "cyborg", "darkly", "slate", "solar", "superhero", "vapor"
            };

        public static bool IsDarkColorScheme(string? theme) =>
            !string.IsNullOrEmpty(theme) && DarkColorScheme.Contains(theme);
    }

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

    public static class DatabaseProviders
    {
        public const string MySql = "MySql";
        public const string Npgsql = "Npgsql";
        public const string Sqlite = "Sqlite";
        public const string SqlServer = "SqlServer";
    }

    public static class HangfireQueues
    {
        public const string Critical = "critical";
        public const string Default = "default";
        public const string Scan = "scan";
        public const string Tts = "tts";
    }

    public static class Roles
    {
        public const string Administrator = "Administrator";
        public const string User = "User";
    }

    public static class Schemas
    {
        public const string App = "app";
    }

    /// <summary>
    /// Default values applied when a setting has never been written. Centralised here so the
    /// theme picker and the layout component can't drift apart.
    /// </summary>
    public static class ServerSettingDefaults
    {
        public const string Theme = "flatly";
    }

    /// <summary>
    /// Well-known keys persisted in the <c>ServerSettings</c> table by
    /// <c>IServerSettingsService</c>. Keep the strings stable — they are the source of truth
    /// once written to the database.
    /// </summary>
    public static class ServerSettingKeys
    {
        public const string SetupComplete = "setup.complete";
        public const string Theme = "ui.theme";
    }
}