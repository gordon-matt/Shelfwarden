namespace Shelfwarden.Models.Opds;

/// <summary>A user's OPDS key, minus the key itself (only its hash is stored).</summary>
public record OpdsCredentialDto(
    string UserId,
    string UserName,
    string KeyPrefix,
    DateTime CreatedAt,
    DateTime? LastUsedAt);

/// <summary>A newly generated key. <see cref="Key"/> is shown once and can't be recovered afterwards.</summary>
public record OpdsKeyIssuedDto(string Key, OpdsCredentialDto Credential)
{
    /// <summary>The key without the readability hyphens, as used in key-in-URL feed addresses.</summary>
    public string CompactKey => Key.Replace("-", string.Empty, StringComparison.Ordinal);
}

/// <summary>Who a valid OPDS key belongs to, for building the request principal.</summary>
public record OpdsIdentity(string UserId, string UserName, IReadOnlyList<string> Roles);
