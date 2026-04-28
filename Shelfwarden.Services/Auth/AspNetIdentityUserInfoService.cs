using Microsoft.AspNetCore.Identity;

namespace Shelfwarden.Services.Auth;

/// <summary>
/// Resolves user info by querying the local ASP.NET Core Identity tables. Used when
/// <c>Authentication:Provider</c> is <c>Identity</c> (the default).
/// </summary>
public class AspNetIdentityUserInfoService(
    IDbContextFactory dbContextFactory,
    UserManager<ApplicationUser> userManager) : IUserInfoService
{
    public async Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.ToHashSet();
        if (ids.Count == 0)
        {
            return new Dictionary<string, UserInfo>();
        }

        var users = await LoadUsersAsync(u => ids.Contains(u.Id), cancellationToken);
        return users.ToDictionary(u => u.Id);
    }

    public async Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(CancellationToken cancellationToken = default)
        => await LoadUsersAsync(_ => true, cancellationToken);

    public async Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var users = await LoadUsersAsync(u => u.Id == userId, cancellationToken);
        return users.FirstOrDefault();
    }

    private async Task<IReadOnlyList<UserInfo>> LoadUsersAsync(
        System.Linq.Expressions.Expression<Func<ApplicationUser, bool>> predicate,
        CancellationToken cancellationToken)
    {
        // We use the DbContextFactory directly so this service works outside an HTTP scope
        // (e.g. from a Hangfire job that captured an IUserInfoService).
        using var context = (ApplicationDbContextBase)dbContextFactory.GetContext();
        var users = await context.Set<ApplicationUser>()
            .AsNoTracking()
            .Where(predicate)
            .OrderBy(u => u.UserName)
            .ToListAsync(cancellationToken);

        var result = new List<UserInfo>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            result.Add(new UserInfo(
                user.Id,
                user.UserName ?? user.Id,
                user.Email,
                user.DisplayName ?? user.UserName,
                roles.ToList()));
        }

        return result;
    }
}