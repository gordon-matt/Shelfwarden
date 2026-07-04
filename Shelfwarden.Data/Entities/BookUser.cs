namespace Shelfwarden.Data.Entities;

/// <summary>
/// Per-user state for a book that isn't tied to a single reading position: the user's own star rating
/// and their free-form notes about the book. Keyed on (BookId, UserId) so each user has at most one row
/// per book.
/// </summary>
public class BookUser : IEntity
{
    public required int BookId { get; set; }

    public required string UserId { get; set; } = null!;

    /// <summary>This user's star rating out of 5. Null means unrated.</summary>
    public byte? Rating { get; set; }

    /// <summary>This user's free-form notes about the book.</summary>
    public string? Notes { get; set; }

    public virtual Book Book { get; set; } = null!;

    public object[] KeyValues => [BookId, UserId];
}

public class BookUserMap : IEntityTypeConfiguration<BookUser>
{
    public void Configure(EntityTypeBuilder<BookUser> builder)
    {
        builder.ToTable("BookUsers", Constants.Schemas.App);
        builder.HasKey(m => new { m.BookId, m.UserId });
        builder.Property(m => m.UserId).IsRequired().HasMaxLength(450);
        builder.Property(m => m.Notes).IsUnicode(true);

        builder.HasOne(m => m.Book)
            .WithMany(b => b.BookUsers)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
