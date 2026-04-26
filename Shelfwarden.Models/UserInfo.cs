namespace Shelfwarden.Models;

/// <summary>Minimal user projection returned by <c>IUserInfoService</c>, regardless of the underlying auth provider.</summary>
public record UserInfo(
    string Id,
    string UserName,
    string? Email,
    string? DisplayName,
    IReadOnlyList<string> Roles)
{
    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
