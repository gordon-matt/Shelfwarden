using Microsoft.AspNetCore.Http.Extensions;

namespace Shelfwarden.Infrastructure;

/// <summary>
/// Bounces every interactive request to <c>/setup</c> until the first-run wizard has been
/// completed. Static files, the wizard itself, the auth endpoints, the Hangfire dashboard,
/// the error page, and a handful of framework paths are exempted so the wizard can render
/// (and so admins can still hit /Identity/* for sign-in within the wizard's flow).
/// <para>
/// Once <c>setup.complete</c> is true the middleware short-circuits to <c>_next</c> so the
/// hot path is one cached dictionary read.
/// </para>
/// </summary>
public class SetupRedirectMiddleware(RequestDelegate next, ILogger<SetupRedirectMiddleware> logger)
{
    private static readonly string[] ExemptPrefixes =
    [
        "/setup",
        "/Identity",
        "/auth",
        "/signin-oidc",
        "/signout-callback-oidc",
        "/css",
        "/js",
        "/lib",
        "/_blazor",
        "/_framework",
        "/_vs",
        "/favicon",
        "/hangfire",
        "/sejil",
        "/error",
        "/access-denied",
    ];

    public async Task InvokeAsync(HttpContext context, IServerSettingsService serverSettings)
    {
        string path = context.Request.Path.Value ?? string.Empty;

        if (IsExempt(path))
        {
            await next(context);
            return;
        }

        var settings = await serverSettings.GetAsync(context.RequestAborted);
        if (settings.IsSuccess && settings.Value.SetupComplete)
        {
            await next(context);
            return;
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Redirecting {Path} to /setup (setup not complete)", context.Request.GetEncodedPathAndQuery());
        }

        // 302 keeps GETs simple. POSTs would be lost but the wizard isn't expected to redirect
        // mid-form-submit; non-exempt POSTs during setup are anomalies (likely bots).
        context.Response.Redirect("/setup");
    }

    private static bool IsExempt(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return false;
        }

        foreach (string prefix in ExemptPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}