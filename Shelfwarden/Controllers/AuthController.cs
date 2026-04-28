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

    [HttpGet("logout")]
    public IActionResult Logout() => authProvider.Provider != AuthProvider.Keycloak
        ? Redirect("/")
        : SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        CookieAuthenticationDefaults.AuthenticationScheme,
        OpenIdConnectDefaults.AuthenticationScheme);
}