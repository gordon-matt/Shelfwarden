using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

/// <summary>
/// Generic key/value store for server-wide settings (theme, scan schedule, first-run wizard, etc.).
/// </summary>
public class ServerSetting : BaseEntity<int>
{
    public required string Key { get; set; }

    public string? Value { get; set; }
}

public class ServerSettingMap : IEntityTypeConfiguration<ServerSetting>
{
    public void Configure(EntityTypeBuilder<ServerSetting> builder)
    {
        builder.ToTable("ServerSettings", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Key).IsRequired().HasMaxLength(128);
        builder.Property(m => m.Value).IsUnicode(true);

        builder.HasIndex(m => m.Key).IsUnique();
    }
}
