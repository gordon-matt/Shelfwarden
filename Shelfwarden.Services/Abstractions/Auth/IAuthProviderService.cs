namespace Shelfwarden.Services.Auth;

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