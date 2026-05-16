namespace Shelfwarden.Data.Entities;

public class SeriesAdditionalContentItem : BaseEntity<int>
{
    public int SeriesId { get; set; }

    public int AdditionalContentItemId { get; set; }

    public virtual Series Series { get; set; } = null!;

    public virtual AdditionalContentItem AdditionalContentItem { get; set; } = null!;
}

public class SeriesAdditionalContentItemMap : IEntityTypeConfiguration<SeriesAdditionalContentItem>
{
    public void Configure(EntityTypeBuilder<SeriesAdditionalContentItem> builder)
    {
        builder.ToTable("SeriesAdditionalContent", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.Series)
            .WithMany(m => m.SeriesAdditionalContent)
            .HasForeignKey(m => m.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.AdditionalContentItem)
            .WithMany(m => m.SeriesAdditionalContents)
            .HasForeignKey(m => m.AdditionalContentItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.SeriesId, m.AdditionalContentItemId }).IsUnique();
    }
}
