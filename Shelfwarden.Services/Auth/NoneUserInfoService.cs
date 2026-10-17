namespace Shelfwarden.Services.Auth;

/// <summary>
/// User info implementation for the "None" authentication mode (desktop / kiosk). There is
/// exactly one synthetic administrator user. The id, name and roles come from the user context,
/// which the authentication package fills from <c>Authentication:None</c>.
/// </summary>
public sealed class NoneUserInfoService(IUserContextService userContext) : IUserInfoService
{
    private UserInfo DefaultUser => new(
        userContext.GetCurrentUserId()!,
        userContext.GetCurrentUserName()!,
        Email: null,
        DisplayName: userContext.GetCurrentUserName()!,
        Roles: [.. userContext.GetRoleNames()]);

    public Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var user = DefaultUser;

        IReadOnlyDictionary<string, UserInfo> result = userIds.Contains(user.Id)
            ? new Dictionary<string, UserInfo> { [user.Id] = user }
            : [];

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<UserInfo>>([DefaultUser]);

    public Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = DefaultUser;

        return Task.FromResult(userId == user.Id ? user : null);
    }
}