namespace Shelfwarden.Data.Entities;

/// <summary>
/// A user's OPDS app key — the password reader apps send instead of the user's real one, so it
/// can be revoked on its own. At most one per user. Only a SHA-256 hash of the key is stored.
/// </summary>
public class OpdsCredential : BaseEntity<int>
{
    /// <summary>
    /// Owning user id. Not a foreign key: Keycloak users have no local <c>Users</c> row.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>User name at the time the key was issued; what reader apps send as the Basic auth user name.</summary>
    public required string UserName { get; set; }

    /// <summary>Lowercase hex SHA-256 of the key.</summary>
    public required string KeyHash { get; set; }

    /// <summary>First few characters of the key, so the user can tell which key a device holds.</summary>
    public required string KeyPrefix { get; set; }

    /// <summary>
    /// Comma-separated role names captured from the user's session. Used for shelf access when the
    /// auth provider can't resolve roles server-side (Keycloak, None); Identity looks them up live.
    /// </summary>
    public string? Roles { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }
}

public class OpdsCredentialMap : IEntityTypeConfiguration<OpdsCredential>
{
    public void Configure(EntityTypeBuilder<OpdsCredential> builder)
    {
        builder.ToTable("OpdsCredentials", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.UserId).IsRequired().HasMaxLength(256);
        builder.Property(m => m.UserName).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.KeyHash).IsRequired().HasMaxLength(64);
        builder.Property(m => m.KeyPrefix).IsRequired().HasMaxLength(16);
        builder.Property(m => m.Roles).HasMaxLength(1024).IsUnicode(true);

        builder.HasIndex(m => m.UserId).IsUnique();
        builder.HasIndex(m => m.KeyHash).IsUnique();
    }
}
