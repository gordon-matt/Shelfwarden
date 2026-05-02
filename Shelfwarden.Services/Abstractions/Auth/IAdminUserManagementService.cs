using Ardalis.Result;
using Shelfwarden.Models;

namespace Shelfwarden.Services.Auth;

/// <summary>
/// Administrator-only operations on local Identity users. Registered only when
/// <c>Authentication:Provider</c> is Identity; Keycloak users are managed externally.
/// </summary>
public interface IAdminUserManagementService
{
    Task<Result<IReadOnlyList<AdminUserListItem>>> ListUsersAsync(CancellationToken cancellationToken = default);

    Task<Result<(string Id, string? Email, string? UserName)>> GetCurrentAccountAsync(
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<RoleOption>>> GetAssignableRolesAsync(CancellationToken cancellationToken = default);

    Task<Result> CreateUserAsync(AdminCreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> UpdateUserAsync(string userId, AdminUpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> ToggleLockoutAsync(string userId, CancellationToken cancellationToken = default);

    Task<Result> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);
}
