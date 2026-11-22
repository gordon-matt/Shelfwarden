namespace Shelfwarden.Services;

/// <summary>
/// Server-wide configuration persisted in the <c>ServerSettings</c> key/value table. Values
/// are cached in memory and invalidated on writes. The first read loads every row; later reads
/// use that cache until the next write. A browser may also remember its own theme.
/// </summary>
public interface IServerSettingsService
{
    /// <summary>Load the typed snapshot of all settings, applying defaults for missing keys.</summary>
    Task<Result<ServerSettingsDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Read a single raw setting by key; returns <see cref="Result.NotFound()"/> when unset.</summary>
    Task<Result<string>> GetRawAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Persist a single setting and invalidate the in-memory cache. Admin-only.</summary>
    Task<Result> SetAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a Bootswatch theme name. A name that is not in the theme list is rejected.
    /// Administrator only.
    /// </summary>
    Task<Result> SetThemeAsync(string theme, CancellationToken cancellationToken = default);
}