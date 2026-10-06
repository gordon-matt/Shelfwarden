using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Shelfwarden.Models.Opds;

namespace Shelfwarden.Services.Opds;

public class OpdsCredentialService(
    ILogger<OpdsCredentialService> logger,
    IUserContextService userContext,
    IAuthProviderService authProvider,
    IUserInfoService userInfoService,
    IMemoryCache cache,
    TimeProvider timeProvider,
    IRepository<OpdsCredential> credentialRepository) : IOpdsCredentialService
{
    /// <summary>Crockford-style base32: no i, l, o or u, so keys survive being typed on an e-reader.</summary>
    public const string KeyAlphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>20 base32 characters = 100 bits of entropy.</summary>
    public const int KeyLength = 20;

    private const int KeyGroupSize = 5;
    private const int KeyPrefixLength = 5;

    private static readonly TimeSpan ValidationCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(5);

    public static string NormalizeKey(string key) =>
        new string([.. key.Where(c => c != '-' && !char.IsWhiteSpace(c))]).ToLowerInvariant();

    public static string HashKey(string normalizedKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedKey)));

    public async Task<Result<OpdsCredentialDto>> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        try
        {
            var credential = await FindByUserAsync(userId, cancellationToken);
            if (credential is null)
            {
                return Result.NotFound();
            }

            string userName = CurrentUserName(userId);
            string roles = CurrentRoles();
            if (credential.UserName != userName || credential.Roles != roles)
            {
                credential.UserName = userName;
                credential.Roles = roles;
                credential = await credentialRepository.UpdateAsync(credential);
                cache.Remove(CacheKey(credential.KeyHash));
            }

            return Result.Success(ToDto(credential));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load OPDS key for user {UserId}", userId);
            return Result.Error("Could not load your OPDS key.");
        }
    }

    public async Task<Result<OpdsKeyIssuedDto>> GenerateAsync(CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        try
        {
            string key = RandomNumberGenerator.GetString(KeyAlphabet, KeyLength);
            string hash = HashKey(key);
            var now = timeProvider.GetUtcNow().UtcDateTime;

            var credential = await FindByUserAsync(userId, cancellationToken);
            if (credential is null)
            {
                credential = await credentialRepository.InsertAsync(new OpdsCredential
                {
                    UserId = userId,
                    UserName = CurrentUserName(userId),
                    Roles = CurrentRoles(),
                    KeyHash = hash,
                    KeyPrefix = key[..KeyPrefixLength],
                    CreatedAt = now,
                });
            }
            else
            {
                cache.Remove(CacheKey(credential.KeyHash));
                credential.UserName = CurrentUserName(userId);
                credential.Roles = CurrentRoles();
                credential.KeyHash = hash;
                credential.KeyPrefix = key[..KeyPrefixLength];
                credential.CreatedAt = now;
                credential.LastUsedAt = null;
                credential = await credentialRepository.UpdateAsync(credential);
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Issued a new OPDS key for user {UserId}", userId);
            }

            return Result.Success(new OpdsKeyIssuedDto(FormatKey(key), ToDto(credential)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to issue OPDS key for user {UserId}", userId);
            return Result.Error("Could not generate an OPDS key.");
        }
    }

    public async Task<Result> RevokeAsync(CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        return string.IsNullOrEmpty(userId)
            ? Result.Unauthorized()
            : await DeleteForUserAsync(userId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<OpdsCredentialDto>>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var rows = await credentialRepository.FindAsync(new SearchOptions<OpdsCredential>
        {
            OrderBy = q => q.OrderBy(c => c.UserName).ThenBy(c => c.Id),
            CancellationToken = cancellationToken,
        });

        return Result.Success<IReadOnlyList<OpdsCredentialDto>>([.. rows.Select(ToDto)]);
    }

    public async Task<Result> RevokeForUserAsync(string userId, CancellationToken cancellationToken = default) =>
        !userContext.IsAdministrator()
            ? Result.Forbidden()
            : string.IsNullOrWhiteSpace(userId)
                ? Result.Invalid(new ValidationError(nameof(userId), "User is required."))
                : await DeleteForUserAsync(userId, cancellationToken);

    public async Task<Result<OpdsIdentity>> ValidateAsync(string? userName, string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Unauthorized();
        }

        string normalized = NormalizeKey(key);
        if (normalized.Length != KeyLength)
        {
            return Result.Unauthorized();
        }

        string hash = HashKey(normalized);
        if (!cache.TryGetValue(CacheKey(hash), out ResolvedKey? resolved) || resolved is null)
        {
            var credential = await credentialRepository.FindOneAsync(new SearchOptions<OpdsCredential>
            {
                Query = c => c.KeyHash == hash,
                CancellationToken = cancellationToken,
            });

            if (credential is null)
            {
                return Result.Unauthorized();
            }

            resolved = await ResolveOwnerAsync(credential, cancellationToken);
            if (resolved is null)
            {
                return Result.Unauthorized();
            }

            cache.Set(CacheKey(hash), resolved, ValidationCacheDuration);
            await TouchAsync(credential);
        }

        if (userName is not null && !resolved.AcceptedUserNames.Contains(userName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return Result.Unauthorized();
        }

        return Result.Success(resolved.Identity);
    }

    private static string CacheKey(string hash) => $"opds-key:{hash}";

    private static string FormatKey(string key) =>
        string.Join('-', key.Chunk(KeyGroupSize).Select(chunk => new string(chunk)));

    private static IReadOnlyList<string> SplitRoles(string? roles) =>
        string.IsNullOrWhiteSpace(roles)
            ? []
            : roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static OpdsCredentialDto ToDto(OpdsCredential c) =>
        new(c.UserId, c.UserName, c.KeyPrefix, c.CreatedAt, c.LastUsedAt);

    private string CurrentRoles() => string.Join(',', userContext.GetRoleNames());

    private string CurrentUserName(string userId) =>
        userContext.GetCurrentUserName() is { Length: > 0 } name ? name : userId;

    private async Task<Result> DeleteForUserAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var credential = await FindByUserAsync(userId, cancellationToken);
            if (credential is null)
            {
                return Result.Success();
            }

            cache.Remove(CacheKey(credential.KeyHash));
            await credentialRepository.DeleteAsync(credential);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Revoked the OPDS key for user {UserId}", userId);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to revoke OPDS key for user {UserId}", userId);
            return Result.Error("Could not revoke the OPDS key.");
        }
    }

    private Task<OpdsCredential?> FindByUserAsync(string userId, CancellationToken cancellationToken) =>
        credentialRepository.FindOneAsync(new SearchOptions<OpdsCredential>
        {
            Query = c => c.UserId == userId,
            CancellationToken = cancellationToken,
        })!;

    /// <summary>
    /// Identity users are looked up live, so deleting or disabling the account cuts the key off and
    /// role changes apply. Keycloak and None have no local user store to ask, so the snapshot
    /// taken when the key was issued (and refreshed on each visit to the OPDS page) is used.
    /// </summary>
    private async Task<ResolvedKey?> ResolveOwnerAsync(OpdsCredential credential, CancellationToken cancellationToken)
    {
        if (authProvider.Provider != AuthProvider.Identity)
        {
            return new ResolvedKey(
                new OpdsIdentity(credential.UserId, credential.UserName, SplitRoles(credential.Roles)),
                [credential.UserName]);
        }

        var user = await userInfoService.GetUserAsync(credential.UserId, cancellationToken);
        if (user is null || user.IsDisabled)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Rejected OPDS key for user {UserId}: account missing or disabled", credential.UserId);
            }

            return null;
        }

        string[] names = [.. new[] { user.UserName, user.Email }.OfType<string>().Where(n => n.Length > 0)];
        return new ResolvedKey(new OpdsIdentity(user.Id, user.UserName, user.Roles), names);
    }

    private async Task TouchAsync(OpdsCredential credential)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (credential.LastUsedAt is { } last && now - last < LastUsedResolution)
        {
            return;
        }

        try
        {
            await credentialRepository.UpdateAsync(
                c => c.Id == credential.Id,
                setters => setters.SetProperty(c => c.LastUsedAt, now));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record OPDS key use for user {UserId}", credential.UserId);
        }
    }

    private sealed record ResolvedKey(OpdsIdentity Identity, IReadOnlyList<string> AcceptedUserNames);
}
