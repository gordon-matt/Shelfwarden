using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

public class Tag : BaseEntity<int>
{
    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public virtual ICollection<BookTag> BookTags { get; set; } = [];
}

public class TagMap : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(128).IsUnicode(true);
        builder.Property(m => m.NormalizedName).IsRequired().HasMaxLength(128).IsUnicode(true);

        builder.HasIndex(m => m.NormalizedName).IsUnique();
    }
}