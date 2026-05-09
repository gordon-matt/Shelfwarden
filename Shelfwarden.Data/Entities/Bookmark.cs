namespace Shelfwarden.Data.Entities;

public class Bookmark : BaseEntity<int>
{
    public required string UserId { get; set; }

    public int BookId { get; set; }

    public string? Title { get; set; }

    public int? PageNumber { get; set; }

    /// <summary>Opaque locator (EPUB CFI for EPUB; null for PDF).</summary>
    public string? Location { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Book Book { get; set; } = null!;
}

public class BookmarkMap : IEntityTypeConfiguration<Bookmark>
{
    public void Configure(EntityTypeBuilder<Bookmark> builder)
    {
        builder.ToTable("Bookmarks", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.UserId).IsRequired().HasMaxLength(450);
        builder.Property(m => m.Title).HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Location).HasMaxLength(1024);
        builder.Property(m => m.Note).HasMaxLength(2048).IsUnicode(true);

        builder.HasOne(m => m.Book)
            .WithMany(m => m.Bookmarks)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.UserId, m.BookId });
    }
}