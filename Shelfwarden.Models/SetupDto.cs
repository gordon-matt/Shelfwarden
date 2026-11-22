namespace Shelfwarden.Models;

/// <summary>
/// Snapshot of first-run wizard state: enough information for the wizard to know which step
/// to render and whether the caller is allowed to advance.
/// </summary>
/// <param name="SetupComplete"><see langword="true"/> once the wizard has been finished.</param>
/// <param name="AuthProvider">Active provider name.</param>
/// <param name="RequiresIdentityAdmin"><see langword="true"/> only when the provider is Identity and the caller is not signed in.</param>
/// <param name="CallerIsAdministrator"><see langword="true"/> when the caller is an administrator.</param>
/// <param name="CallerIsAuthenticated"><see langword="true"/> when someone is signed in.</param>
/// <param name="ShelfCount">Shelves that already exist.</param>
public record SetupStatusDto(
    bool SetupComplete,
    string AuthProvider,
    bool RequiresIdentityAdmin,
    bool CallerIsAdministrator,
    bool CallerIsAuthenticated,
    int ShelfCount);

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