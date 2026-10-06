using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shelfwarden.Infrastructure.Opds;
using Shelfwarden.Models;
using Shelfwarden.Models.Opds;
using Shelfwarden.Services;
using Shelfwarden.Services.Opds;

namespace Shelfwarden.Tests.Opds;

/// <summary>
/// What a reading app sees at the HTTP level: a Basic challenge it can answer, 404 while the
/// catalogue is switched off, and 429 once a client keeps guessing.
/// </summary>
public class OpdsAuthenticationHandlerTests
{
    private const string UserName = "reader";
    private const string GoodKey = "abcde-fghjk-mnpqr-stvwx";

    private readonly Mock<IOpdsCredentialService> credentials = new();
    private readonly Mock<IServerSettingsService> settings = new();
    private readonly OpdsAuthThrottle throttle;

    public OpdsAuthenticationHandlerTests()
    {
        SetEnabled(true);

        credentials.Setup(x => x.ValidateAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OpdsIdentity>.Unauthorized());
        credentials.Setup(x => x.ValidateAsync(It.Is<string?>(u => u == null || u == UserName), GoodKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new OpdsIdentity("user-1", UserName, [Constants.Roles.User])));

        throttle = new OpdsAuthThrottle(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new OpdsOptions { MaxFailedAuthAttempts = 3, FailedAuthWindow = TimeSpan.FromMinutes(10) }),
            TimeProvider.System);
    }

    [Fact]
    public async Task No_credentials_gets_a_basic_challenge()
    {
        var (result, response) = await RunAsync(_ => { });

        Assert.True(result.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"Shelfwarden OPDS\", charset=\"UTF-8\"", response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task Valid_basic_credentials_sign_the_user_in()
    {
        var (result, _) = await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, GoodKey));

        Assert.True(result.Succeeded);
        var principal = result.Principal!;
        Assert.Equal("user-1", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(UserName, principal.Identity?.Name);
        Assert.True(principal.IsInRole(Constants.Roles.User));
        Assert.Equal(OpdsAuthenticationHandler.SchemeName, principal.Identity?.AuthenticationType);
    }

    [Fact]
    public async Task Passwords_containing_colons_are_kept_whole()
    {
        await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, "part:two"));

        credentials.Verify(x => x.ValidateAsync(UserName, "part:two", It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_key_in_the_route_signs_in_without_a_header()
    {
        var (result, _) = await RunAsync(c => c.Request.RouteValues[OpdsAuthenticationHandler.ApiKeyRouteValue] = GoodKey);

        Assert.True(result.Succeeded);
        credentials.Verify(x => x.ValidateAsync(null, GoodKey, It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData("Basic not-base64!")]
    [InlineData("Bearer abc")]
    [InlineData("Basic Om5vdXNlcg==")]
    public async Task Malformed_authorization_headers_are_challenged(string header)
    {
        var (result, response) = await RunAsync(c => c.Request.Headers.Authorization = header);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_credentials_are_challenged()
    {
        var (result, response) = await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, "wrong"));

        Assert.NotNull(result.Failure);
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_catalogue_answers_not_found_even_with_valid_credentials()
    {
        SetEnabled(false);

        var (result, response) = await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, GoodKey));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.False(response.Headers.ContainsKey("WWW-Authenticate"));
        credentials.Verify(x => x.ValidateAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Repeated_failures_are_throttled_per_client_and_user()
    {
        for (int i = 0; i < 3; i++)
        {
            await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, "wrong"));
        }

        // Now blocked, even with the right key, and without asking the credential store.
        credentials.Invocations.Clear();
        var (blocked, response) = await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, GoodKey));

        Assert.False(blocked.Succeeded);
        Assert.Equal(StatusCodes.Status429TooManyRequests, response.StatusCode);
        Assert.True(int.Parse(response.Headers.RetryAfter.ToString()) > 0);
        credentials.VerifyNoOtherCalls();

        // A different address, or a different user name from the same address, isn't affected.
        var (otherAddress, _) = await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, GoodKey), "10.0.0.99");
        Assert.True(otherAddress.Succeeded);

        var (_, otherUser) = await RunAsync(c => c.Request.Headers.Authorization = Basic("someone-else", "wrong"));
        Assert.Equal(StatusCodes.Status401Unauthorized, otherUser.StatusCode);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_earlier_failures()
    {
        for (int i = 0; i < 2; i++)
        {
            await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, "wrong"));
        }

        Assert.True((await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, GoodKey))).Result.Succeeded);

        for (int i = 0; i < 2; i++)
        {
            await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, "wrong"));
        }

        Assert.True((await RunAsync(c => c.Request.Headers.Authorization = Basic(UserName, GoodKey))).Result.Succeeded);
    }

    [Theory]
    [InlineData("/opds/key/abcdefghjkmnpqrstvwx/books/1/download/x.epub", "/opds/key/***/books/1/download/x.epub")]
    [InlineData("/base/OPDS/KEY/secret", "/base/OPDS/KEY/***")]
    [InlineData("/opds/key/secret?page=2", "/opds/key/***?page=2")]
    [InlineData("/opds/recent", "/opds/recent")]
    [InlineData("/books/key/notopds", "/books/key/notopds")]
    public void Keys_in_paths_are_redacted_from_logs(string path, string expected)
    {
        Assert.Equal(expected, OpdsLogRedaction.RedactPath(path));
    }

    private static string Basic(string user, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

    private void SetEnabled(bool enabled) =>
        settings.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ServerSettingsDto { Theme = "flatly", SetupComplete = true, OpdsEnabled = enabled }));

    /// <summary>Authenticates one request and, when that doesn't succeed, issues the challenge as the authorization middleware would.</summary>
    private async Task<(AuthenticateResult Result, HttpResponse Response)> RunAsync(Action<HttpContext> configure, string remoteAddress = "10.0.0.1")
    {
        var schemeOptions = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        schemeOptions.Setup(x => x.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

        var handler = new OpdsAuthenticationHandler(
            schemeOptions.Object,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            settings.Object,
            credentials.Object,
            throttle);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        configure(context);

        await handler.InitializeAsync(
            new AuthenticationScheme(OpdsAuthenticationHandler.SchemeName, null, typeof(OpdsAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();
        if (!result.Succeeded)
        {
            await handler.ChallengeAsync(new AuthenticationProperties());
        }

        return (result, context.Response);
    }
}
