namespace Shelfwarden.Data.Entities;

public class AdditionalContentItemTag : IEntity
{
    public int ItemId { get; set; }

    public int TagId { get; set; }

    public virtual AdditionalContentItem Item { get; set; } = null!;

    public virtual AdditionalContentTag Tag { get; set; } = null!;

    public object[] KeyValues => [ItemId, TagId];
}

public class AdditionalContentItemTagMap : IEntityTypeConfiguration<AdditionalContentItemTag>
{
    public void Configure(EntityTypeBuilder<AdditionalContentItemTag> builder)
    {
        builder.ToTable("AdditionalContentItemTags", Constants.Schemas.App);
        builder.HasKey(m => new { m.ItemId, m.TagId });

        builder.HasOne(m => m.Item)
            .WithMany(m => m.AdditionalContentItemTags)
            .HasForeignKey(m => m.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Tag)
            .WithMany(m => m.AdditionalContentItemTags)
            .HasForeignKey(m => m.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.ItemId, m.TagId }).IsUnique();
    }
}