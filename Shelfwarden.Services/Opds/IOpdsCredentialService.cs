using Shelfwarden.Models.Opds;

namespace Shelfwarden.Services.Opds;

/// <summary>
/// Issues, revokes and checks OPDS keys: per-user app passwords that reader apps use instead of the
/// user's real credentials. Each user has at most one key; generating a new one revokes the old.
/// </summary>
public interface IOpdsCredentialService
{
    /// <summary>
    /// The calling user's key details, or not found. Also re-captures the user name and roles
    /// from the live session, which is how role changes reach keys under Keycloak / None auth.
    /// </summary>
    Task<Result<OpdsCredentialDto>> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Issues a new key for the calling user, replacing any existing one.</summary>
    Task<Result<OpdsKeyIssuedDto>> GenerateAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes the calling user's key. Succeeds when there is none.</summary>
    Task<Result> RevokeAsync(CancellationToken cancellationToken = default);

    /// <summary>Every issued key. Administrator-only.</summary>
    Task<Result<IReadOnlyList<OpdsCredentialDto>>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes another user's key. Administrator-only.</summary>
    Task<Result> RevokeForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the user a key belongs to. Hyphens, whitespace and case in the key are ignored.
    /// <paramref name="userName"/> is the HTTP Basic user name, matched case-insensitively (Identity
    /// accepts the user name or email); pass null when the key came from the URL. Valid results are
    /// cached briefly, so revoking or disabling a user can take up to a minute to bite.
    /// </summary>
    Task<Result<OpdsIdentity>> ValidateAsync(string? userName, string key, CancellationToken cancellationToken = default);
}
