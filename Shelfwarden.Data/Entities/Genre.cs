using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

public class Genre : BaseEntity<int>
{
    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public virtual ICollection<BookGenre> BookGenres { get; set; } = [];
}

public class GenreMap : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("Genres", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(128).IsUnicode(true);
        builder.Property(m => m.NormalizedName).IsRequired().HasMaxLength(128).IsUnicode(true);

        builder.HasIndex(m => m.NormalizedName).IsUnique();
    }
}
