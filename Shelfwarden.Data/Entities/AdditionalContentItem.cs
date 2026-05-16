namespace Shelfwarden.Data.Entities;

public class AdditionalContentItem : BaseEntity<int>
{
    /// <summary>Display name (may differ from the filename if the user has renamed it).</summary>
    public required string FileName { get; set; }

    /// <summary>Absolute path of the file on disk.</summary>
    public required string FilePath { get; set; }

    /// <summary>Lowercase file extension including the leading dot (e.g. ".pdf").</summary>
    public required string FileExtension { get; set; }

    public long FileSizeBytes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When true, the file was registered from an arbitrary path (not discovered under
    /// <c>_extras</c>). Scanning must not delete or relocate this row; deletes should not remove the file from disk.
    /// </summary>
    public bool IsManuallyImported { get; set; }

    /// <summary>
    /// The author this content is attributed to. Nullable while the item is unassigned
    /// (i.e. freshly discovered on disk but not yet associated by an admin).
    /// </summary>
    public int? AuthorId { get; set; }

    public virtual Author? Author { get; set; }

    public virtual ICollection<BookAdditionalContentItem> BookAdditionalContents { get; set; } = [];

    public virtual ICollection<SeriesAdditionalContentItem> SeriesAdditionalContents { get; set; } = [];
}

public class AdditionalContentItemMap : IEntityTypeConfiguration<AdditionalContentItem>
{
    public void Configure(EntityTypeBuilder<AdditionalContentItem> builder)
    {
        builder.ToTable("AdditionalContent", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.FileName).IsRequired().HasMaxLength(512).IsUnicode(true);
        builder.Property(m => m.FilePath).IsRequired().HasMaxLength(1024).IsUnicode(true);
        builder.Property(m => m.FileExtension).IsRequired().HasMaxLength(32);
        builder.Property(m => m.IsManuallyImported).HasDefaultValue(false);

        builder.HasIndex(m => m.FilePath).IsUnique();
        builder.HasIndex(m => m.AuthorId);

        builder.HasOne(m => m.Author)
            .WithMany(m => m.AdditionalContent)
            .HasForeignKey(m => m.AuthorId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
