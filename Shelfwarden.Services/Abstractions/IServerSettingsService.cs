namespace Shelfwarden.Services;

/// <summary>
/// Server-wide configuration persisted in the <c>ServerSettings</c> key/value table. Values
/// are cached in memory and invalidated on writes; reads on the hot path hit memory after the
/// first request. (UI theme is per-browser in localStorage, not this store.)
/// </summary>
public interface IServerSettingsService
{
    /// <summary>Load the typed snapshot of all settings, applying defaults for missing keys.</summary>
    Task<Result<ServerSettingsDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Read a single raw setting by key; returns <see cref="Result.NotFound()"/> when unset.</summary>
    Task<Result<string>> GetRawAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Persist a single setting and invalidate the in-memory cache. Admin-only.</summary>
    Task<Result> SetAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>Convenience wrapper that validates against <see cref="Constants.BootswatchThemes.All"/>.</summary>
    Task<Result> SetThemeAsync(string theme, CancellationToken cancellationToken = default);
}