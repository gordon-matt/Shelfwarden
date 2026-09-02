namespace Shelfwarden.Data.Entities;

/// <summary>
/// A book's membership of a universe. Universe-specific data (where the book sits on the
/// timeline) lives on the relationship rather than on <see cref="Book"/>, so the same book can
/// later belong to more than one universe without touching the book record.
/// </summary>
public class UniverseBook : BaseEntity<int>
{
    public int UniverseId { get; set; }

    public int BookId { get; set; }

    /// <summary>
    /// Free-text in-universe date shown on the timeline (e.g. "10,191 AG", "Spring 1998",
    /// "2350–2352", "Before the Fall"). Never parsed — <see cref="TimelineOrder"/> decides ordering.
    /// </summary>
    public string? TimelineDate { get; set; }

    /// <summary>Position on the universe timeline. Compacted to a contiguous 0..n-1 range.</summary>
    public int TimelineOrder { get; set; }

    public virtual Universe Universe { get; set; } = null!;

    public virtual Book Book { get; set; } = null!;
}

public class UniverseBookMap : IEntityTypeConfiguration<UniverseBook>
{
    public void Configure(EntityTypeBuilder<UniverseBook> builder)
    {
        builder.ToTable("UniverseBooks", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TimelineDate).HasMaxLength(50).IsUnicode(true);

        builder.HasOne(m => m.Universe)
            .WithMany(m => m.UniverseBooks)
            .HasForeignKey(m => m.UniverseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Book)
            .WithMany(m => m.UniverseBooks)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.UniverseId, m.BookId }).IsUnique();
        builder.HasIndex(m => new { m.UniverseId, m.TimelineOrder });
    }
}
