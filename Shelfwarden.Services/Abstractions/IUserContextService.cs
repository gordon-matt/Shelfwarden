namespace Shelfwarden.Services;

/// <summary>
/// Wraps access to the currently signed-in user. Implementations resolve identity from
/// either <see cref="Microsoft.AspNetCore.Http.HttpContext"/> or, for the desktop "None"
/// auth mode, from a synthetic default user.
/// </summary>
public interface IUserContextService
{
    string? GetCurrentUserId();

    string? GetCurrentUserName();

    bool IsAuthenticated();

    bool IsAdministrator();
}
