namespace Shelfwarden.Data.Entities;

public class BookAuthor : BaseEntity<int>
{
    public int BookId { get; set; }

    public int AuthorId { get; set; }

    /// <summary>Order of authors on the cover (0 = first author). Defaults to 0.</summary>
    public int Position { get; set; }

    public virtual Book Book { get; set; } = null!;

    public virtual Author Author { get; set; } = null!;
}

public class BookAuthorMap : IEntityTypeConfiguration<BookAuthor>
{
    public void Configure(EntityTypeBuilder<BookAuthor> builder)
    {
        builder.ToTable("BookAuthors", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.Book)
            .WithMany(m => m.BookAuthors)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Author)
            .WithMany(m => m.BookAuthors)
            .HasForeignKey(m => m.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.BookId, m.AuthorId }).IsUnique();
    }
}