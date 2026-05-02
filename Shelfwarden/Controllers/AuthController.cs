using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace Shelfwarden.Controllers;

/// <summary>
/// Hosts the Keycloak login/logout endpoints. Only mapped when
/// <c>Authentication:Provider</c> is <c>Keycloak</c>; for ASP.NET Identity the
/// Identity UI Razor pages handle this, and for <c>None</c> there's nothing to do.
/// </summary>
[Route("auth")]
public class AuthController(IAuthProviderService authProvider) : Controller
{
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null) => authProvider.Provider != AuthProvider.Keycloak
        ? Redirect(returnUrl ?? "/")
        : Challenge(new AuthenticationProperties
        {
            RedirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl,
        }, OpenIdConnectDefaults.AuthenticationScheme);

    /// <summary>
    /// Keycloak OIDC sign-out must run before clearing the cookie (so id_token_hint is
    /// available) and must not return <see cref="RedirectResult"/>, which would overwrite
    /// the end-session redirect to Keycloak (same pattern as Kinnect's AuthController).
    /// </summary>
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> LogoutPost() => LogoutAsync();

    /// <summary>Allows bookmarked or legacy GET <c>/auth/logout</c> when not using POST.</summary>
    [HttpGet("logout")]
    public Task<IActionResult> LogoutGet() => LogoutAsync();

    private async Task<IActionResult> LogoutAsync()
    {
        if (authProvider.Provider != AuthProvider.Keycloak)
        {
            return Redirect("/");
        }

        var oidcProps = new AuthenticationProperties { RedirectUri = "/" };
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, oidcProps);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return new EmptyResult();
    }
}