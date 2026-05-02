using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

public class Shelf : BaseEntity<int>
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>UTC timestamp of the most recent successful scan, or null when never scanned.</summary>
    public DateTime? LastScannedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<ShelfFolder> Folders { get; set; } = [];

    public virtual ICollection<Book> Books { get; set; } = [];

    /// <summary>When non-empty (together with <see cref="RoleAccessEntries"/>), only matching users/roles may view this shelf.</summary>
    public virtual ICollection<ShelfUserAccess> UserAccessEntries { get; set; } = [];

    public virtual ICollection<ShelfRoleAccess> RoleAccessEntries { get; set; } = [];
}

public class ShelfMap : IEntityTypeConfiguration<Shelf>
{
    public void Configure(EntityTypeBuilder<Shelf> builder)
    {
        builder.ToTable("Shelves", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Description).HasMaxLength(2048).IsUnicode(true);

        builder.HasIndex(m => m.Name).IsUnique();
    }
}
