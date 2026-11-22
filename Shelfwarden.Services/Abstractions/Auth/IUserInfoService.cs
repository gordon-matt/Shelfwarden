namespace Shelfwarden.Services.Auth;

/// <summary>
/// Provider-agnostic service for looking up basic user information by ID. Decouples the
/// rest of the service layer from the concrete auth provider (ASP.NET Identity vs Keycloak
/// vs the synthetic "None" user used by the desktop build), since navigation properties to
/// <c>ApplicationUser</c> are not always available.
/// </summary>
public interface IUserInfoService
{
    /// <summary>
    /// Returns the users for the supplied ids, keyed by id. An id that matches no user is left out.
    /// An empty list returns an empty dictionary.
    /// </summary>
    /// <remarks>
    /// Identity includes each user's roles and whether lockout is still in effect. None returns the
    /// single synthetic user, and only when that id was requested; the email is
    /// <see langword="null"/>. Keycloak returns no roles, reads at most 1000 users, and returns an
    /// empty dictionary when it cannot be queried.
    /// </remarks>
    /// <param name="userIds">Ids to look up. An empty sequence returns an empty dictionary.</param>
    /// <param name="cancellationToken">Cancels the query for Identity and Keycloak. The no-auth provider does not observe it.</param>
    /// <returns>Users keyed by id. Unknown ids are omitted.</returns>
    Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>Returns every user the active provider can list.</summary>
    /// <remarks>
    /// Identity returns every local user, ordered by user name, with roles and lockout. None returns
    /// the single synthetic user, and its email is <see langword="null"/>. Keycloak returns at most
    /// 1000 users and no roles, or an empty list when it cannot be queried.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query for Identity and Keycloak. The no-auth provider does not observe it.</param>
    /// <returns>Every user the active provider can list.</returns>
    Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user with this id, or <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// When Keycloak cannot be queried, the result is <see langword="null"/>. None returns the
    /// synthetic user only when the id matches, and that user's email is <see langword="null"/>.
    /// </remarks>
    /// <param name="userId">User id.</param>
    /// <param name="cancellationToken">Cancels the query for Identity and Keycloak. The no-auth provider does not observe it.</param>
    /// <returns>The user, or <see langword="null"/> when there is none.</returns>
    Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
}