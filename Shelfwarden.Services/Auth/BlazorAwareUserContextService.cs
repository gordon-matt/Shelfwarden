using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace Shelfwarden.Services.Auth;

/// <summary>
/// Resolves the current user like <see cref="UserContextService"/>, but when interactive Blazor
/// runs work inside the SignalR circuit, <see cref="IHttpContextAccessor.HttpContext"/> is often
/// <c>null</c>, so role checks would incorrectly fail. Falls back to
/// <see cref="AuthenticationStateProvider"/> which reflects the same principal used by
/// <c>AuthorizeView</c> / <c>[Authorize]</c> on components.
/// </summary>
public sealed class BlazorAwareUserContextService(
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider) : IUserContextService
{
    public string? GetCurrentUserId() => ResolvePrincipal()?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? GetCurrentUserName() => ResolvePrincipal()?.Identity?.Name;

    public bool IsAuthenticated() => ResolvePrincipal()?.Identity?.IsAuthenticated ?? false;

    public bool IsAdministrator() => ResolvePrincipal()?.IsInRole(Constants.Roles.Administrator) ?? false;

    public IReadOnlyList<string> GetRoleNames()
    {
        var user = ResolvePrincipal();
        return user is null
            ? []
            : (IReadOnlyList<string>)user.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private ClaimsPrincipal? ResolvePrincipal()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            return httpContext.User;
        }

        // Interactive Blazor: circuit dispatch — HttpContext is frequently unset here even though
        // Hangfire/Sejil (plain HTTP requests) still see the authenticated user on HttpContext.
        try
        {
            var user = Task.Run(async () =>
                    (await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false)).User)
                .GetAwaiter().GetResult();

            return user.Identity?.IsAuthenticated == true ? user : httpContext?.User;
        }
        catch
        {
            return httpContext?.User;
        }
    }
}