namespace Shelfwarden.Data.Entities;

/// <summary>A genre shared by the whole library. Names match without case.</summary>
public class Genre : BaseEntity<int>
{
    public required string Name { get; set; }

    /// <summary>Lowercased name. Unique, so two genres cannot differ only by case.</summary>
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