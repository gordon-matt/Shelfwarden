namespace Shelfwarden.Services.Auth;

public class AuthProviderService(AuthProvider provider, string? keycloakAuthority) : IAuthProviderService
{
    public AuthProvider Provider { get; } = provider;

    public string? KeycloakAuthority { get; } = keycloakAuthority;
}