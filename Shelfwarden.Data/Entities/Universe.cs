namespace Shelfwarden.Data.Entities;

/// <summary>
/// A fictional universe that groups related <see cref="Series"/> and <see cref="Book"/>s together
/// and owns the reading orders (<see cref="ReadingList"/>) and timeline (<see cref="UniverseBook"/>)
/// for that setting. Universes are catalog data like <see cref="Series"/> — visible to everyone,
/// maintained by administrators.
/// </summary>
public class Universe : BaseEntity<int>
{
    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Series> Series { get; set; } = [];

    public virtual ICollection<UniverseBook> UniverseBooks { get; set; } = [];

    /// <summary>Reading orders scoped to this universe (publication order, chronological order, …).</summary>
    public virtual ICollection<ReadingList> ReadingLists { get; set; } = [];
}

public class UniverseMap : IEntityTypeConfiguration<Universe>
{
    public void Configure(EntityTypeBuilder<Universe> builder)
    {
        builder.ToTable("Universes", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.NormalizedName).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Description).IsUnicode(true);

        builder.HasIndex(m => m.NormalizedName).IsUnique();
    }
}
