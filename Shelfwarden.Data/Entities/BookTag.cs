using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

public class BookTag : BaseEntity<int>
{
    public int BookId { get; set; }

    public int TagId { get; set; }

    public virtual Book Book { get; set; } = null!;

    public virtual Tag Tag { get; set; } = null!;
}

public class BookTagMap : IEntityTypeConfiguration<BookTag>
{
    public void Configure(EntityTypeBuilder<BookTag> builder)
    {
        builder.ToTable("BookTags", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.Book)
            .WithMany(m => m.BookTags)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Tag)
            .WithMany(m => m.BookTags)
            .HasForeignKey(m => m.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.BookId, m.TagId }).IsUnique();
    }
}
