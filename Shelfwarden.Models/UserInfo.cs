namespace Shelfwarden.Models;

/// <summary>
/// A user, whatever the active sign-in provider is.
/// <paramref name="IsDisabled"/> is <see langword="true"/> only when Identity still has the account locked out.
/// Other providers leave it <see langword="false"/>.
/// </summary>
/// <param name="Id">User id from the active provider.</param>
/// <param name="UserName">Sign-in name. Identity and Keycloak use <paramref name="Id"/> when the provider has no user name.</param>
/// <param name="Email">Email, or <see langword="null"/>. The no-auth provider always leaves this empty.</param>
/// <param name="DisplayName">Name to show, or <see langword="null"/>.</param>
/// <param name="Roles">Role names. Empty when the provider does not supply them.</param>
/// <param name="IsDisabled">
/// <see langword="true"/> when Identity lockout has not ended. Keycloak and the no-auth provider leave this <see langword="false"/>.
/// </param>
public record UserInfo(
    string Id,
    string UserName,
    string? Email,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    bool IsDisabled = false)
{
    /// <summary>Whether <paramref name="role"/> is one of <see cref="Roles"/>, compared without case.</summary>
    /// <param name="role">Role name to test.</param>
    /// <returns><see langword="true"/> when the user has that role.</returns>
    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}