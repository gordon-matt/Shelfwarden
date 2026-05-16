namespace Shelfwarden.Data.Entities;

public class AdditionalContentTag : BaseEntity<int>
{
    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public virtual ICollection<AdditionalContentItemTag> AdditionalContentItemTags { get; set; } = [];
}

public class AdditionalContentTagMap : IEntityTypeConfiguration<AdditionalContentTag>
{
    public void Configure(EntityTypeBuilder<AdditionalContentTag> builder)
    {
        builder.ToTable("AdditionalContentTags", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(128).IsUnicode(true);
        builder.Property(m => m.NormalizedName).IsRequired().HasMaxLength(128).IsUnicode(true);

        builder.HasIndex(m => m.NormalizedName).IsUnique();
    }
}