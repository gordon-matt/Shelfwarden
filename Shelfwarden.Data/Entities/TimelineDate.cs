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

    /// <summary>
    /// Free-text in-universe label (e.g. "10,191 AG", "Before the Fall"). Used when the universe
    /// is <see cref="TimelineType.Named"/>. Null on numeric dates — those are labelled from
    /// <see cref="YearFrom"/>/<see cref="YearTo"/> instead.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional numeric start year, for universes whose dates are (or can be approximated as)
    /// real numbers rather than free text. When set, the universe is <see cref="TimelineType.Numeric"/>
    /// and the timeline renders on a proportional axis so overlapping ranges become visible.
    /// Negative values (BC/BCE-style eras) sort correctly since they're plain integers.
    /// </summary>
    public int? YearFrom { get; set; }

    /// <summary>Optional numeric end year. Falls back to <see cref="YearFrom"/> for a point-in-time date.</summary>
    public int? YearTo { get; set; }

    /// <summary>
    /// Position on the universe timeline. Compacted to a contiguous 0..n-1 range. Acts as the sort
    /// key when the universe is <see cref="TimelineType.Named"/>, and as a tie-breaker between
    /// dates that share the same <see cref="YearFrom"/>/<see cref="YearTo"/> otherwise.
    /// </summary>
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
        builder.Property(m => m.Name).HasMaxLength(50).IsUnicode(true);

        builder.HasOne(m => m.Universe)
            .WithMany(m => m.TimelineDates)
            .HasForeignKey(m => m.UniverseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.UniverseId, m.Order });
    }
}
