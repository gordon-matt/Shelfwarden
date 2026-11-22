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
    /// <summary>
    /// Returns whether setup is complete, the authentication provider's name, whether the caller is
    /// signed in and an administrator, and how many shelves exist.
    /// <see cref="SetupStatusDto.RequiresIdentityAdmin"/> is <see langword="true"/> only when the
    /// provider is <see cref="AuthProvider.Identity"/> and the caller is not signed in.
    /// </summary>
    /// <remarks>
    /// When the settings cannot be read, setup is reported as not complete.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The wizard snapshot.</returns>
    Task<Result<SetupStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the initial shelf, bypassing the admin check. Idempotent on duplicate names —
    /// returns Conflict instead of throwing. Refuses with Conflict once setup is already done.
    /// </summary>
    /// <param name="request">The shelf. At least one folder is required.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The shelf, or invalid or conflict.</returns>
    Task<Result<ShelfDto>> CreateInitialShelfAsync(CreateShelfRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the wizard complete. Becomes a no-op if already complete. Bypasses the
    /// admin check on the assumption that the caller has just walked through the wizard
    /// (the redirect middleware also bounces unauthenticated calls to <c>/setup</c> to the
    /// shelf step).
    /// </summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success.</returns>
    Task<Result> CompleteAsync(CancellationToken cancellationToken = default);
}