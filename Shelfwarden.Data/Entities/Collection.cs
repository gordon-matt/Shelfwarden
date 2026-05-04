using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfwarden.Models;

namespace Shelfwarden.Data.Entities;

/// <summary>
/// A user-curated grouping of books. Use <see cref="Constants.GlobalUserId"/> for collections
/// that should be visible to everyone.
/// </summary>
public class Collection : BaseEntity<int>
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public required string OwnerUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CardHeaderBannerMode CardBannerMode { get; set; }

    public string? CardBannerImageFileName { get; set; }

    public string? CardBannerBookIdsJson { get; set; }

    public virtual ICollection<CollectionBook> CollectionBooks { get; set; } = [];
}

public class CollectionMap : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Description).IsUnicode(true);
        builder.Property(m => m.OwnerUserId).IsRequired().HasMaxLength(450);
        builder.Property(m => m.CardBannerImageFileName).HasMaxLength(256);
        builder.Property(m => m.CardBannerBookIdsJson).HasMaxLength(512);

        builder.HasIndex(m => new { m.OwnerUserId, m.Name }).IsUnique();
    }
}