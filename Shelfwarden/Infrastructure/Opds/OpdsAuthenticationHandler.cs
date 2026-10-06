using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shelfwarden.Services.Opds;

namespace Shelfwarden.Infrastructure.Opds;

/// <summary>
/// Authenticates OPDS clients, which can't do cookie or OIDC sign-in. Accepts either HTTP Basic
/// (user name + OPDS key) or a key embedded in the route (<c>/opds/key/{apiKey}/...</c>) for
/// clients that can't send credentials. Registered in every auth mode and only used by
/// <c>OpdsController</c>.
/// <para>
/// While OPDS is switched off the challenge answers 404 so the catalogue looks absent; repeated
/// failures from one client are answered 429. Keys and Authorization headers are never logged.
/// </para>
/// </summary>
public sealed class OpdsAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IServerSettingsService serverSettings,
    IOpdsCredentialService credentialService,
    OpdsAuthThrottle throttle) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "Opds";
    public const string Realm = "Shelfwarden OPDS";
    public const string ApiKeyRouteValue = "apiKey";

    private enum Outcome { None, Disabled, Throttled }

    private Outcome outcome;
    private TimeSpan retryAfter;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var settings = await serverSettings.GetAsync(Context.RequestAborted);
        if (!settings.IsSuccess || !settings.Value.OpdsEnabled)
        {
            outcome = Outcome.Disabled;
            return AuthenticateResult.NoResult();
        }

        string? userName = null;
        string? key = Request.RouteValues[ApiKeyRouteValue] as string;
        if (string.IsNullOrEmpty(key) && !TryReadBasicCredentials(out userName, out key))
        {
            return AuthenticateResult.NoResult();
        }

        string remoteAddress = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string clientKey = $"{remoteAddress}|{userName?.ToUpperInvariant()}";

        if (throttle.GetBlockedFor(clientKey) is { } blockedFor)
        {
            outcome = Outcome.Throttled;
            retryAfter = blockedFor;
            return AuthenticateResult.Fail("Too many failed OPDS sign-in attempts.");
        }

        var result = await credentialService.ValidateAsync(userName, key!, Context.RequestAborted);
        if (!result.IsSuccess)
        {
            throttle.RecordFailure(clientKey);
            Logger.LogWarning(
                "OPDS sign-in failed for {UserName} from {RemoteAddress}",
                userName ?? "(key in URL)",
                remoteAddress);
            return AuthenticateResult.Fail("Invalid OPDS credentials.");
        }

        throttle.Reset(clientKey);

        var identity = result.Value;
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, identity.UserId),
            new(ClaimTypes.Name, identity.UserName),
            .. identity.Roles.Select(role => new Claim(ClaimTypes.Role, role)),
        ];

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        switch (outcome)
        {
            case Outcome.Disabled:
                Response.StatusCode = StatusCodes.Status404NotFound;
                break;

            case Outcome.Throttled:
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;

            default:
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                Response.Headers.WWWAuthenticate = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";
                break;
        }

        return Task.CompletedTask;
    }

    private bool TryReadBasicCredentials(out string? userName, out string? key)
    {
        userName = null;
        key = null;

        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !"Basic".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        int separator = decoded.IndexOf(':');
        if (separator <= 0 || separator == decoded.Length - 1)
        {
            return false;
        }

        userName = decoded[..separator];
        key = decoded[(separator + 1)..];
        return true;
    }
}
