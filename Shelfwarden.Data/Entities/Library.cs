using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

public class Library : BaseEntity<int>
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>UTC timestamp of the most recent successful scan, or null when never scanned.</summary>
    public DateTime? LastScannedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<LibraryFolder> Folders { get; set; } = [];

    public virtual ICollection<Book> Books { get; set; } = [];
}

public class LibraryMap : IEntityTypeConfiguration<Library>
{
    public void Configure(EntityTypeBuilder<Library> builder)
    {
        builder.ToTable("Libraries", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Description).HasMaxLength(2048).IsUnicode(true);

        builder.HasIndex(m => m.Name).IsUnique();
    }
}