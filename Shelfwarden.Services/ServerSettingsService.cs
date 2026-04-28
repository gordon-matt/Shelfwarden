using System.Collections.Concurrent;

namespace Shelfwarden.Services;

/// <summary>
/// Default <see cref="IServerSettingsService"/>. Caches all settings in a process-wide
/// concurrent dictionary; cache is invalidated on every write. The cache is per-process,
/// which is fine for a single-instance deployment — multi-instance scenarios should add a
/// distributed cache invalidation strategy later.
/// </summary>
public class ServerSettingsService(
    ILogger<ServerSettingsService> logger,
    IUserContextService userContext,
    IRepository<ServerSetting> repository) : IServerSettingsService
{
    private static readonly ConcurrentDictionary<string, string?> cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim loadLock = new(1, 1);
    private static volatile bool isLoaded;

    /// <summary>
    /// Forces the next <see cref="GetAsync"/> to re-read all settings from the database. Used by
    /// the first-run wizard which writes <c>setup.complete</c> via the bare repository (bypassing
    /// the admin check that <see cref="SetAsync"/> enforces).
    /// </summary>
    public static void InvalidateCache()
    {
        cache.Clear();
        isLoaded = false;
    }

    public async Task<Result<ServerSettingsDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        var dto = new ServerSettingsDto
        {
            Theme = NullIfWhitespace(GetCached(Constants.ServerSettingKeys.Theme)) ?? Constants.ServerSettingDefaults.Theme,
            SetupComplete = bool.TryParse(GetCached(Constants.ServerSettingKeys.SetupComplete), out bool b) && b,
        };

        return Result.Success(dto);
    }

    public async Task<Result<string>> GetRawAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Invalid(new ValidationError(nameof(key), "Key is required."));
        }

        await EnsureLoadedAsync(cancellationToken);

        string? value = GetCached(key);
        return string.IsNullOrEmpty(value)
            ? Result.NotFound($"Setting '{key}' is not set.")
            : Result.Success(value);
    }

    public async Task<Result> SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Invalid(new ValidationError(nameof(key), "Key is required."));
        }

        var existing = await repository.FindOneAsync(new SearchOptions<ServerSetting>
        {
            Query = s => s.Key == key,
            CancellationToken = cancellationToken,
        });

        if (existing is null)
        {
            await repository.InsertAsync(new ServerSetting { Key = key, Value = value });
        }
        else
        {
            existing.Value = value;
            await repository.UpdateAsync(existing);
        }

        cache[key] = value;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Server setting '{Key}' updated", key);
        }

        return Result.Success();
    }

    public Task<Result> SetThemeAsync(string theme, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(theme) ||
        !Constants.BootswatchThemes.All.Contains(theme, StringComparer.OrdinalIgnoreCase)
            ? Task.FromResult<Result>(Result.Invalid(
                new ValidationError(nameof(theme), $"'{theme}' is not a known Bootswatch theme.")))
            : SetAsync(Constants.ServerSettingKeys.Theme, theme.ToLowerInvariant(), cancellationToken);

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (isLoaded)
        {
            return;
        }

        await loadLock.WaitAsync(cancellationToken);
        try
        {
            if (isLoaded)
            {
                return;
            }

            var rows = await repository.FindAsync(new SearchOptions<ServerSetting>
            {
                CancellationToken = cancellationToken,
            });
            foreach (var row in rows)
            {
                cache[row.Key] = row.Value;
            }
            isLoaded = true;
        }
        finally
        {
            loadLock.Release();
        }
    }

    private static string? GetCached(string key) => cache.TryGetValue(key, out string? v) ? v : null;

    private static string? NullIfWhitespace(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}