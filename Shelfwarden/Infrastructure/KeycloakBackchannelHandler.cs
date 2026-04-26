namespace Shelfwarden.Infrastructure;

/// <summary>
/// Reroutes Keycloak backchannel HTTP calls (discovery + token exchange) from the public
/// authority URL to an internal hostname. Required when the app and Keycloak share a Docker
/// network and the public hostname isn't resolvable from inside the container.
/// </summary>
public class KeycloakBackchannelHandler : HttpClientHandler
{
    private readonly Uri _publicAuthority;
    private readonly Uri _internalBase;

    public KeycloakBackchannelHandler(string publicAuthority, string internalBaseUrl)
    {
        _publicAuthority = new Uri(publicAuthority.TrimEnd('/') + "/");
        _internalBase = new Uri(internalBaseUrl.TrimEnd('/') + "/");
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null && IsUnderPublicAuthority(request.RequestUri))
        {
            string relative = _publicAuthority.MakeRelativeUri(request.RequestUri).ToString();
            request.RequestUri = new Uri(_internalBase, relative);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private bool IsUnderPublicAuthority(Uri uri)
        => string.Equals(uri.Host, _publicAuthority.Host, StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith(_publicAuthority.AbsolutePath, StringComparison.OrdinalIgnoreCase);
}
