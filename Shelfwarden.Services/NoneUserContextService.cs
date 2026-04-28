namespace Shelfwarden.Services;

/// <summary>
/// User context implementation for the "None" authentication mode. Always returns the
/// synthetic default user (<see cref="Constants.DefaultUserId"/>) and reports it as an
/// authenticated administrator. Wired up by the desktop project.
/// </summary>
public class NoneUserContextService : IUserContextService
{
    public string GetCurrentUserId() => Constants.DefaultUserId;

    public string GetCurrentUserName() => Constants.DefaultUserName;

    public bool IsAuthenticated() => true;

    public bool IsAdministrator() => true;

    string? IUserContextService.GetCurrentUserId() => GetCurrentUserId();

    string? IUserContextService.GetCurrentUserName() => GetCurrentUserName();
}