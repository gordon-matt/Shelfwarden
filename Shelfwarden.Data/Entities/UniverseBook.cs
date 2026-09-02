namespace Shelfwarden.Data.Entities;

/// <summary>
/// A book's membership of a universe. Universe-specific data lives on the relationship rather
/// than on <see cref="Book"/>, so the same book can later belong to more than one universe
/// without touching the book record. Where the book sits on the timeline is delegated to
/// <see cref="TimelineDate"/> — several books can share one — with <see cref="Order"/> deciding
/// their sequence within that one date.
/// </summary>
public class UniverseBook : BaseEntity<int>
{
    public int UniverseId { get; set; }

    public int BookId { get; set; }

    /// <summary>
    /// The timeline date this book sits on, or <c>null</c> while it's still unscheduled. Books
    /// join a universe unscheduled and stay that way until someone picks a date for them.
    /// </summary>
    public int? TimelineDateId { get; set; }

    /// <summary>Position among the books sharing this book's date. Compacted to 0..n-1 per date.</summary>
    public int Order { get; set; }

    public virtual Universe Universe { get; set; } = null!;

    public virtual Book Book { get; set; } = null!;

    public virtual TimelineDate? TimelineDate { get; set; }
}

public class UniverseBookMap : IEntityTypeConfiguration<UniverseBook>
{
    public void Configure(EntityTypeBuilder<UniverseBook> builder)
    {
        builder.ToTable("UniverseBooks", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.Universe)
            .WithMany(m => m.UniverseBooks)
            .HasForeignKey(m => m.UniverseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Book)
            .WithMany(m => m.UniverseBooks)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a date un-schedules its books rather than evicting them from the universe.
        builder.HasOne(m => m.TimelineDate)
            .WithMany(t => t.UniverseBooks)
            .HasForeignKey(m => m.TimelineDateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => new { m.UniverseId, m.BookId }).IsUnique();
        builder.HasIndex(m => new { m.TimelineDateId, m.Order });
    }
}
