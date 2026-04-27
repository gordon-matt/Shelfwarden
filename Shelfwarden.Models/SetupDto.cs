using System.ComponentModel.DataAnnotations;

namespace Shelfwarden.Models;

/// <summary>
/// Snapshot of first-run wizard state: enough information for the wizard to know which step
/// to render and whether the caller is allowed to advance.
/// </summary>
public record SetupStatusDto(
    bool SetupComplete,
    string AuthProvider,
    bool RequiresIdentityAdmin,
    bool CallerIsAdministrator,
    bool CallerIsAuthenticated,
    int LibraryCount);

/// <summary>Form posted to <c>POST /setup/identity-admin</c> by the Identity-mode wizard step.</summary>
public record SetupAdminRequest
{
    [Required, EmailAddress, StringLength(320)]
    public required string Email { get; init; }

    [Required, MinLength(6), StringLength(128)]
    public required string Password { get; init; }

    [Required, StringLength(128)]
    public string DisplayName { get; init; } = "Administrator";
}
