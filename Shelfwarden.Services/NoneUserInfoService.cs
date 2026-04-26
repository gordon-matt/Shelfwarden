namespace Shelfwarden.Services;

/// <summary>
/// User info implementation for the "None" authentication mode (desktop / kiosk). There is
/// exactly one synthetic administrator user, identified by <see cref="Constants.DefaultUserId"/>.
/// </summary>
public class NoneUserInfoService : IUserInfoService
{
    private static readonly UserInfo DefaultUser = new(
        Constants.DefaultUserId,
        Constants.DefaultUserName,
        Email: null,
        DisplayName: Constants.DefaultUserName,
        Roles: [Constants.Roles.Administrator]);

    public Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, UserInfo> result = userIds.Contains(Constants.DefaultUserId)
            ? new Dictionary<string, UserInfo> { [Constants.DefaultUserId] = DefaultUser }
            : new Dictionary<string, UserInfo>();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<UserInfo>>([DefaultUser]);

    public Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserInfo?>(userId == Constants.DefaultUserId ? DefaultUser : null);
}
