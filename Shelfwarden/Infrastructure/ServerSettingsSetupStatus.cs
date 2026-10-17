using Heroicsoft.NetForge.Abstractions.Setup;

namespace Shelfwarden.Infrastructure;

/// <summary>
/// Tells the shared setup gate whether the first-run wizard has been completed. The answer is the
/// <c>setup.complete</c> server setting, which <see cref="ServerSettingsService"/> keeps in a
/// process-wide cache, so the per-request check does not touch the database once settings are loaded.
/// </summary>
public sealed class ServerSettingsSetupStatus(IServerSettingsService serverSettings) : ISetupStatus
{
    public async Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default)
    {
        var settings = await serverSettings.GetAsync(cancellationToken);

        return settings.IsSuccess && settings.Value.SetupComplete;
    }
}