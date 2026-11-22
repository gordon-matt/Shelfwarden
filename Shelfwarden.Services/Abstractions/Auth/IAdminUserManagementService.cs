namespace Shelfwarden.Services.Auth;

/// <summary>
/// Administrator-only operations on local Identity users. Registered only when
/// <c>Authentication:Provider</c> is Identity; Keycloak users are managed externally.
/// </summary>
public interface IAdminUserManagementService
{
    /// <summary>
    /// Returns every local user, ordered by email when they have one and otherwise by user name.
    /// A user counts as active when lockout has no end or that end is before now. Each user's roles
    /// are ordered by name. The display name falls back to the user name, and the user name falls
    /// back to the id.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Every local user.</returns>
    Task<Result<IReadOnlyList<AdminUserListItem>>> ListUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the signed-in user's id, email, and user name. Returns unauthorized when nobody is
    /// signed in. An id that Identity does not have is not found.
    /// </summary>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>The account, or unauthorized or not found.</returns>
    Task<Result<(string Id, string? Email, string? UserName)>> GetCurrentAccountAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Returns every Identity role, ordered by name.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Every role, not only the two that create and update accept.</returns>
    Task<Result<IReadOnlyList<RoleOption>>> GetAssignableRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a local user. The user name and email are the trimmed email, and the email is marked
    /// confirmed. A blank email is rejected. An empty password is rejected, but a password that is
    /// only whitespace is accepted. A blank role becomes
    /// <see cref="WellKnownRoles.User"/>. Only that role and <see cref="WellKnownRoles.Administrator"/>
    /// are accepted, matched exactly. Identity's own failures are returned as invalid, and a user
    /// whose role could not be assigned is deleted again.
    /// </summary>
    /// <param name="request">Email, password, and role.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Success, or invalid.</returns>
    Task<Result> CreateUserAsync(AdminCreateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes another user's email, user name, and role. The signed-in user cannot change their own
    /// account. A missing user is not found. A blank email is rejected. A blank role becomes
    /// <see cref="WellKnownRoles.User"/>, and only that role or <see cref="WellKnownRoles.Administrator"/>
    /// is accepted, matched exactly. The email and user name are set to the trimmed email. The user's
    /// current roles are removed and the chosen role is added.
    /// </summary>
    /// <param name="userId">User to change. The signed-in user cannot change their own account.</param>
    /// <param name="request">New email and role.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Success, or invalid or not found.</returns>
    /// <remarks>
    /// Adding the role is not checked, so a failed add still returns success after the old roles
    /// were removed.
    /// </remarks>
    Task<Result> UpdateUserAsync(string userId, AdminUpdateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks or unlocks another user. The signed-in user cannot lock their own account. A missing
    /// user is not found. A user who is not locked out is locked until <see cref="DateTimeOffset.MaxValue"/>,
    /// and lockout is enabled. A user who is already locked out has the lockout end cleared.
    /// </summary>
    /// <param name="userId">User to lock or unlock. The signed-in user cannot lock their own account.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Success, or invalid or not found.</returns>
    Task<Result> ToggleLockoutAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes another local user. The signed-in user cannot delete their own account. A missing user
    /// is not found. Identity's own failures are returned as invalid.
    /// </summary>
    /// <param name="userId">User to delete. The signed-in user cannot delete their own account.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Success, or invalid or not found.</returns>
    Task<Result> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);
}