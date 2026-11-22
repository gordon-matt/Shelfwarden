namespace Shelfwarden.Models.Opds;

/// <summary>A user's OPDS key, minus the key itself (only its hash is stored).</summary>
/// <param name="UserId">User the key belongs to.</param>
/// <param name="UserName">That user's sign-in name.</param>
/// <param name="KeyPrefix">First characters of the key, so a list can tell keys apart. Not enough to authenticate.</param>
/// <param name="CreatedAt">When the key was issued, in UTC.</param>
/// <param name="LastUsedAt">When a feed was last fetched with it, or <see langword="null"/>.</param>
public record OpdsCredentialDto(
    string UserId,
    string UserName,
    string KeyPrefix,
    DateTime CreatedAt,
    DateTime? LastUsedAt);

/// <summary>A newly generated key. <see cref="Key"/> is shown once and can't be recovered afterwards.</summary>
/// <param name="Key">The key, with hyphens for reading. This is the only time it is returned.</param>
/// <param name="Credential">The stored row, which does not contain <paramref name="Key"/>.</param>
public record OpdsKeyIssuedDto(string Key, OpdsCredentialDto Credential)
{
    /// <summary>The key without the readability hyphens, as used in key-in-URL feed addresses.</summary>
    public string CompactKey => Key.Replace("-", string.Empty, StringComparison.Ordinal);
}

/// <summary>Who a valid OPDS key belongs to, for building the request principal.</summary>
/// <param name="UserId">User id.</param>
/// <param name="UserName">Sign-in name.</param>
/// <param name="Roles">Role names copied onto the principal.</param>
public record OpdsIdentity(string UserId, string UserName, IReadOnlyList<string> Roles);