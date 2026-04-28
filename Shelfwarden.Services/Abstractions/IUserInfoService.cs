namespace Shelfwarden.Services;

/// <summary>
/// Provider-agnostic service for looking up basic user information by ID. Decouples the
/// rest of the service layer from the concrete auth provider (ASP.NET Identity vs Keycloak
/// vs the synthetic "None" user used by the desktop build), since navigation properties to
/// <c>ApplicationUser</c> are not always available.
/// </summary>
public interface IUserInfoService
{
    Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(
        CancellationToken cancellationToken = default);

    Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
}