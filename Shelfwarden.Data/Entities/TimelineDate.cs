namespace Shelfwarden.Data.Entities;

/// <summary>
/// A named point (or free-text range, e.g. "2350-2352") on a universe's timeline, maintained
/// explicitly by an administrator rather than inferred from what someone typed against a book.
/// Any number of <see cref="UniverseBook"/> rows can point at the same date, which is what lets
/// several books line up as "the same moment".
/// </summary>
public class TimelineDate : BaseEntity<int>
{
    public int UniverseId { get; set; }

    /// <summary>Free-text in-universe date/range (e.g. "10,191 AG", "2350-2352"). Never parsed.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Position on the universe timeline. Compacted to a contiguous 0..n-1 range.</summary>
    public int Order { get; set; }

    public virtual Universe Universe { get; set; } = null!;

    public virtual ICollection<UniverseBook> UniverseBooks { get; set; } = [];
}

public class TimelineDateMap : IEntityTypeConfiguration<TimelineDate>
{
    public void Configure(EntityTypeBuilder<TimelineDate> builder)
    {
        builder.ToTable("TimelineDates", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Date).HasMaxLength(50).IsUnicode(true).IsRequired();

        builder.HasOne(m => m.Universe)
            .WithMany(m => m.TimelineDates)
            .HasForeignKey(m => m.UniverseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.UniverseId, m.Order });
    }
}
