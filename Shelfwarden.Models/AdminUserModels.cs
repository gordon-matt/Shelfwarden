namespace Shelfwarden.Models;

/// <summary>
/// A local Identity user. <paramref name="IsActive"/> is <see langword="true"/> when lockout has no end, or that end is before now.
/// </summary>
/// <param name="Id">Identity user id.</param>
/// <param name="UserName">Sign-in name. Falls back to <paramref name="Id"/> when Identity has none.</param>
/// <param name="Email">Email, or <see langword="null"/>.</param>
/// <param name="DisplayName">Name to show. Falls back to <paramref name="UserName"/>.</param>
/// <param name="EmailConfirmed">Whether Identity has confirmed the email. Create marks it confirmed.</param>
/// <param name="IsActive"><see langword="true"/> when lockout has no end, or that end is before now.</param>
/// <param name="Roles">Role names, ordered by name.</param>
public record AdminUserListItem(
    string Id,
    string UserName,
    string? Email,
    string? DisplayName,
    bool EmailConfirmed,
    bool IsActive,
    IReadOnlyList<string> Roles);

/// <summary>
/// A local user to create. The email is also the user name, and it is marked confirmed.
/// A blank email is rejected. An empty password is rejected, but a password of only whitespace is accepted.
/// A blank <paramref name="Role"/> becomes <c>User</c>. Only <c>User</c> and <c>Administrator</c> are accepted, matched exactly.
/// </summary>
/// <param name="Email">Email, also stored as the user name. Blank is rejected. It is trimmed.</param>
/// <param name="Password">Empty is rejected. A password of only whitespace is accepted.</param>
/// <param name="Role">Role name. Blank becomes <c>User</c>. Only <c>User</c> and <c>Administrator</c> match.</param>
public record AdminCreateUserRequest(string Email, string Password, string? Role);

/// <summary>
/// Email and role for another local user. The signed-in user cannot change their own account.
/// A blank <see cref="Role"/> becomes <c>User</c>. Only <c>User</c> and <c>Administrator</c> are accepted, matched exactly.
/// The previous roles are removed before the chosen role is added, and a failed add still returns success.
/// </summary>
/// <param name="Email">New email, also stored as the user name. Blank is rejected. It is trimmed.</param>
/// <param name="Role">Role name. Blank becomes <c>User</c>. Only <c>User</c> and <c>Administrator</c> match.</param>
public record AdminUpdateUserRequest(string Email, string? Role);

/// <summary>
/// An Identity role as stored. The admin role list includes every role. Creating or editing a user still accepts only <c>User</c> and <c>Administrator</c>.
/// </summary>
/// <param name="Id">Identity role id.</param>
/// <param name="Name">Role name.</param>
public record RoleOption(string Id, string Name);