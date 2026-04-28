using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

public class BookGenre : BaseEntity<int>
{
    public int BookId { get; set; }

    public int GenreId { get; set; }

    public virtual Book Book { get; set; } = null!;

    public virtual Genre Genre { get; set; } = null!;
}

public class BookGenreMap : IEntityTypeConfiguration<BookGenre>
{
    public void Configure(EntityTypeBuilder<BookGenre> builder)
    {
        builder.ToTable("BookGenres", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.Book)
            .WithMany(m => m.BookGenres)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Genre)
            .WithMany(m => m.BookGenres)
            .HasForeignKey(m => m.GenreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.BookId, m.GenreId }).IsUnique();
    }
}