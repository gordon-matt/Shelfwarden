using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using NETCore.Keycloak.Client.HttpClients.Implementation;
using NETCore.Keycloak.Client.Models.Auth;
using NETCore.Keycloak.Client.Models.Users;

namespace Shelfwarden.Services;

/// <summary>
/// User info service backed by the Keycloak Admin REST API. Used when
/// <c>Authentication:Provider</c> is <c>Keycloak</c>. Requires a confidential client whose
/// service account holds the <c>realm-management/view-users</c> role. Configure with
/// <c>Authentication:Keycloak:AdminClientId</c> / <c>AdminClientSecret</c> (or falls back
/// to the main <c>ClientId</c> / <c>ClientSecret</c>).
/// </summary>
public class KeycloakUserInfoService(
    IConfiguration configuration,
    ILogger<KeycloakUserInfoService> logger) : IUserInfoService
{
    private record AdminConfig(string BaseUrl, string Realm, string ClientId, string ClientSecret);

    public async Task<IReadOnlyDictionary<string, UserInfo>> GetUserInfoAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToHashSet();
        if (ids.Count == 0)
        {
            return new Dictionary<string, UserInfo>();
        }

        var allUsers = await FetchAllUsersAsync(cancellationToken);
        return allUsers
            .Where(u => ids.Contains(u.Id))
            .ToDictionary(u => u.Id);
    }

    public Task<IReadOnlyList<UserInfo>> GetAllUsersAsync(CancellationToken cancellationToken = default)
        => FetchAllUsersAsync(cancellationToken);

    public async Task<UserInfo?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var users = await FetchAllUsersAsync(cancellationToken);
        return users.FirstOrDefault(u => u.Id == userId);
    }

    private async Task<IReadOnlyList<UserInfo>> FetchAllUsersAsync(CancellationToken cancellationToken)
    {
        var config = GetAdminConfig();
        if (config is null)
        {
            return [];
        }

        string? token = await GetAdminTokenAsync(config, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        try
        {
            var client = new KeycloakClient(config.BaseUrl);
            var response = await client.Users.ListUserAsync(
                config.Realm,
                token,
                new KcUserFilter { Max = 1000 },
                cancellationToken);

            if (response.IsError || response.Response is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to list Keycloak users: {Error}", response.ErrorMessage);
                }

                return [];
            }

            return response.Response
                .Where(u => u.Id is not null)
                .Select(u => new UserInfo(
                    u.Id!,
                    u.UserName ?? u.Id!,
                    u.Email,
                    BuildDisplayName(u),
                    Roles: []))
                .ToList();
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Exception listing Keycloak users");
            }

            return [];
        }
    }

    private async Task<string?> GetAdminTokenAsync(AdminConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var client = new KeycloakClient(config.BaseUrl);
            var response = await client.Auth.GetClientCredentialsTokenAsync(
                config.Realm,
                new KcClientCredentials { ClientId = config.ClientId, Secret = config.ClientSecret },
                cancellationToken);

            if (response.IsError || response.Response is null)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed to obtain Keycloak admin token: {Error}", response.ErrorMessage);
                }

                return null;
            }

            return response.Response.AccessToken;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(ex, "Exception obtaining Keycloak admin token");
            }

            return null;
        }
    }

    private AdminConfig? GetAdminConfig()
    {
        string? authority = configuration["Authentication:Keycloak:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            return null;
        }

        string baseUrl = ExtractBaseUrl(authority);
        string realm = ExtractRealm(authority);
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(realm))
        {
            return null;
        }

        string clientId = configuration["Authentication:Keycloak:AdminClientId"]
            ?? configuration["Authentication:Keycloak:ClientId"]
            ?? string.Empty;

        string clientSecret = configuration["Authentication:Keycloak:AdminClientSecret"]
            ?? configuration["Authentication:Keycloak:ClientSecret"]
            ?? string.Empty;

        return string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)
            ? null
            : new AdminConfig(baseUrl, realm, clientId, clientSecret);
    }

    private static string ExtractBaseUrl(string authority)
    {
        int idx = authority.IndexOf("/realms/", StringComparison.Ordinal);
        return idx > 0 ? authority[..idx] : authority;
    }

    private static string ExtractRealm(string authority)
    {
        var match = Regex.Match(authority, @"/realms/([^/?#]+)");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string BuildDisplayName(KcUser user)
    {
        string fullName = $"{user.FirstName} {user.LastName}".Trim();
        return !string.IsNullOrEmpty(fullName) ? fullName : user.UserName ?? user.Id ?? string.Empty;
    }
}