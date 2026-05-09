namespace Shelfwarden.Data.Entities;

/// <summary>
/// Tracks how far a particular user has read into a particular book.
/// </summary>
public class BookProgress : BaseEntity<int>
{
    public required string UserId { get; set; }

    public int BookId { get; set; }

    /// <summary>0-100, percentage through the book.</summary>
    public double Percentage { get; set; }

    /// <summary>1-based page number for PDF, or logical reading-order index for EPUB.</summary>
    public int? PageNumber { get; set; }

    /// <summary>Opaque locator for resuming (EPUB CFI / chapter+offset for EPUB; null for PDF).</summary>
    public string? Location { get; set; }

    public DateTime LastReadAt { get; set; } = DateTime.UtcNow;

    public virtual Book Book { get; set; } = null!;
}

public class BookProgressMap : IEntityTypeConfiguration<BookProgress>
{
    public void Configure(EntityTypeBuilder<BookProgress> builder)
    {
        builder.ToTable("BookProgress", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.UserId).IsRequired().HasMaxLength(450);
        builder.Property(m => m.Location).HasMaxLength(1024);

        builder.HasOne(m => m.Book)
            .WithMany(m => m.ReadingProgress)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.UserId, m.BookId }).IsUnique();
    }
}