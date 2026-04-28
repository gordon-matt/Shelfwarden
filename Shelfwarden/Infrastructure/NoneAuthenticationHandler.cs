using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Shelfwarden.Infrastructure;

/// <summary>
/// Authentication handler used when <c>Authentication:Provider</c> is <c>None</c>. Every
/// request is authenticated as a synthetic administrator user identified by
/// <see cref="Constants.DefaultUserId"/>. This is intended for the desktop / kiosk build
/// where there is exactly one local user and no login flow.
/// </summary>
public class NoneAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "None";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Constants.DefaultUserId),
                new Claim(ClaimTypes.Name, Constants.DefaultUserName),
                new Claim(ClaimTypes.Role, Constants.Roles.Administrator),
                new Claim(ClaimTypes.Role, Constants.Roles.User),
            ],
            authenticationType: SchemeName,
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}