namespace Shelfwarden.Services;

public enum AuthProvider
{
    Identity = 0,
    Keycloak = 1,
    None = 2,
}

/// <summary>Singleton snapshot of which auth provider is active and any provider-specific config (e.g. Keycloak authority).</summary>
public interface IAuthProviderService
{
    AuthProvider Provider { get; }

    string? KeycloakAuthority { get; }
}

public class AuthProviderService(AuthProvider provider, string? keycloakAuthority) : IAuthProviderService
{
    public AuthProvider Provider { get; } = provider;

    public string? KeycloakAuthority { get; } = keycloakAuthority;
}