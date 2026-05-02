namespace Shelfwarden.Services;

/// <summary>
/// First-run wizard helper. Wraps the few operations that need to bypass the normal
/// administrator gate during initial bootstrap (creating the inaugural shelf, marking
/// setup complete) and exposes a snapshot of where the caller is in the flow.
/// <para>
/// All mutating methods refuse to run once <see cref="Constants.ServerSettingKeys.SetupComplete"/>
/// is true — the wizard is single-shot and must not become a privilege-escalation surface
/// after install.
/// </para>
/// </summary>
public interface ISetupService
{
    Task<Result<SetupStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the initial shelf, bypassing the admin check. Idempotent on duplicate names —
    /// returns Conflict instead of throwing. Refuses with Conflict once setup is already done.
    /// </summary>
    Task<Result<ShelfDto>> CreateInitialShelfAsync(CreateShelfRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the wizard complete. Becomes a no-op if already complete. Bypasses the
    /// admin check on the assumption that the caller has just walked through the wizard
    /// (the redirect middleware also bounces unauthenticated calls to <c>/setup</c> to the
    /// shelf step).
    /// </summary>
    Task<Result> CompleteAsync(CancellationToken cancellationToken = default);
}