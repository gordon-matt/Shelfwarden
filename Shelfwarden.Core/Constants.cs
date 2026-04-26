namespace Shelfwarden;

public static class Constants
{
    /// <summary>
    /// Synthetic user id used by the "None" authentication mode (desktop / kiosk scenarios).
    /// </summary>
    public const string DefaultUserId = "_default";

    /// <summary>
    /// Display name used for the synthetic user when no real auth provider is active.
    /// </summary>
    public const string DefaultUserName = "Default User";

    /// <summary>
    /// Sentinel UserId stored on records that are global / system-wide rather than owned
    /// by a specific user (e.g. a built-in collection visible to everyone).
    /// </summary>
    public const string GlobalUserId = "_global";

    public static class Roles
    {
        public const string Administrator = "Administrator";
        public const string User = "User";
    }

    public static class AuthProviders
    {
        public const string Identity = "Identity";
        public const string Keycloak = "Keycloak";
        public const string None = "None";
    }

    public static class DatabaseProviders
    {
        public const string Sqlite = "Sqlite";
        public const string SqlServer = "SqlServer";
        public const string Npgsql = "Npgsql";
        public const string MySql = "MySql";
    }

    public static class HangfireQueues
    {
        public const string Default = "default";
        public const string Critical = "critical";
        public const string Scan = "scan";
    }

    public static class Schemas
    {
        public const string App = "app";
    }
}
