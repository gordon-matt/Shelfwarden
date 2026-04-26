using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Shelfwarden.Services;

namespace Shelfwarden.Controllers;

/// <summary>
/// Hosts the Keycloak login/logout endpoints. Only mapped when
/// <c>Authentication:Provider</c> is <c>Keycloak</c>; for ASP.NET Identity the
/// Identity UI Razor pages handle this, and for <c>None</c> there's nothing to do.
/// </summary>
[Microsoft.AspNetCore.Mvc.Route("auth")]
public class AuthController(IAuthProviderService authProvider) : Controller
{
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (authProvider.Provider != AuthProvider.Keycloak)
        {
            return Redirect(returnUrl ?? "/");
        }

        return Challenge(new AuthenticationProperties
        {
            RedirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl,
        }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("logout")]
    public IActionResult Logout()
    {
        if (authProvider.Provider != AuthProvider.Keycloak)
        {
            return Redirect("/");
        }

        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
