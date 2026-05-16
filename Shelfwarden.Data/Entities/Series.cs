namespace Shelfwarden.Data.Entities;

public class Series : BaseEntity<int>
{
    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public string? Description { get; set; }

    public virtual ICollection<Book> Books { get; set; } = [];

    public virtual ICollection<SeriesAdditionalContentItem> SeriesAdditionalContent { get; set; } = [];
}

public class SeriesMap : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder.ToTable("Series", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.NormalizedName).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Description).IsUnicode(true);

        builder.HasIndex(m => m.NormalizedName).IsUnique();
    }
}