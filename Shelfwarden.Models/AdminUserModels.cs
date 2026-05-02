namespace Shelfwarden.Models;

/// <summary>Row model for the administrator user table when using ASP.NET Identity.</summary>
public record AdminUserListItem(
    string Id,
    string UserName,
    string? Email,
    string? DisplayName,
    bool EmailConfirmed,
    bool IsActive,
    IReadOnlyList<string> Roles);

public record AdminCreateUserRequest(string Email, string Password, string? Role);

public record AdminUpdateUserRequest(string Email, string? Role);

public record RoleOption(string Id, string Name);
