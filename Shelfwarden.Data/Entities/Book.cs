using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

public class Book : BaseEntity<int>
{
    public required string Title { get; set; }

    public string? SortTitle { get; set; }

    public string? Subtitle { get; set; }

    public string? Description { get; set; }

    public string? Language { get; set; }

    public string? Publisher { get; set; }

    public string? Isbn { get; set; }

    public DateTime? PublishedOn { get; set; }

    public int? PageCount { get; set; }

    /// <summary>Absolute path of the ebook file on disk.</summary>
    public required string FilePath { get; set; }

    public long FileSizeBytes { get; set; }

    public EbookFormat FileFormat { get; set; } = EbookFormat.Unknown;

    /// <summary>Relative path under the covers directory (e.g. "covers/123.jpg"); null if none extracted yet.</summary>
    public string? CoverImagePath { get; set; }

    public DateTime? FileLastModified { get; set; }

    public DateTime? LastScannedAt { get; set; }

    /// <summary>
    /// Timestamp of the last user metadata edit ("reviewed" state). Null means the book has
    /// never been reviewed and still only carries scanner-imported metadata.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int LibraryId { get; set; }

    public int? SeriesId { get; set; }

    /// <summary>Position within the series. Nullable decimal so 1, 1.5, 2 etc. all work.</summary>
    public decimal? NumberInSeries { get; set; }

    public virtual Library Library { get; set; } = null!;

    public virtual Series? Series { get; set; }

    public virtual ICollection<BookAuthor> BookAuthors { get; set; } = [];

    public virtual ICollection<BookGenre> BookGenres { get; set; } = [];

    public virtual ICollection<BookTag> BookTags { get; set; } = [];

    public virtual ICollection<BookProgress> ReadingProgress { get; set; } = [];

    public virtual ICollection<Bookmark> Bookmarks { get; set; } = [];
}

public class BookMap : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Books", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Title).IsRequired().HasMaxLength(512).IsUnicode(true);
        builder.Property(m => m.SortTitle).HasMaxLength(512).IsUnicode(true);
        builder.Property(m => m.Subtitle).HasMaxLength(512).IsUnicode(true);
        builder.Property(m => m.Description).IsUnicode(true);
        builder.Property(m => m.Language).HasMaxLength(16);
        builder.Property(m => m.Publisher).HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Isbn).HasMaxLength(32);
        builder.Property(m => m.FilePath).IsRequired().HasMaxLength(1024).IsUnicode(true);
        builder.Property(m => m.CoverImagePath).HasMaxLength(1024);
        builder.Property(m => m.NumberInSeries).HasPrecision(10, 2);

        builder.HasIndex(m => new { m.LibraryId, m.FilePath });
        builder.HasIndex(m => m.SeriesId);
        builder.HasIndex(m => m.Title);

        builder.HasOne(m => m.Library)
            .WithMany(m => m.Books)
            .HasForeignKey(m => m.LibraryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Series)
            .WithMany(m => m.Books)
            .HasForeignKey(m => m.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}