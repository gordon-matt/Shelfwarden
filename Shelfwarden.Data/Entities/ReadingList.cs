namespace Shelfwarden.Data.Entities;

public class ReadingList : BaseEntity<int>, ICardBannerOwner
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public required string OwnerUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CardHeaderBannerMode CardBannerMode { get; set; }

    public string? CardBannerImageFileName { get; set; }

    public string? CardBannerBookIdsJson { get; set; }

    /// <summary>
    /// Set when this list is a universe reading order (publication / chronological / …). Those
    /// lists are managed from the universe page and are hidden from the normal reading lists UI.
    /// </summary>
    public int? UniverseId { get; set; }

    public virtual Universe? Universe { get; set; }

    public virtual ICollection<ReadingListItem> Items { get; set; } = [];
}

public class ReadingListMap : IEntityTypeConfiguration<ReadingList>
{
    public void Configure(EntityTypeBuilder<ReadingList> builder)
    {
        builder.ToTable("ReadingLists", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Description).IsUnicode(true);
        builder.Property(m => m.OwnerUserId).IsRequired().HasMaxLength(450);
        builder.Property(m => m.CardBannerImageFileName).HasMaxLength(256);
        builder.Property(m => m.CardBannerBookIdsJson).HasMaxLength(512);

        // Universe reading orders are owned by the global user, so the same name ("Chronological
        // Order") has to be usable in more than one universe — hence UniverseId in the key.
        builder.HasIndex(m => new { m.OwnerUserId, m.UniverseId, m.Name }).IsUnique();

        // Deleting a reading list must not touch the universe, and vice versa the lists go with it.
        builder.HasOne(m => m.Universe)
            .WithMany(m => m.ReadingLists)
            .HasForeignKey(m => m.UniverseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}