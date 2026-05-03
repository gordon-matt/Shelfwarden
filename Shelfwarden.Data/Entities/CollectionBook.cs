using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

public class CollectionBook : BaseEntity<int>
{
    public int CollectionId { get; set; }

    public int BookId { get; set; }

    public virtual Collection Collection { get; set; } = null!;

    public virtual Book Book { get; set; } = null!;
}

public class CollectionBookMap : IEntityTypeConfiguration<CollectionBook>
{
    public void Configure(EntityTypeBuilder<CollectionBook> builder)
    {
        builder.ToTable("CollectionBooks", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.Collection)
            .WithMany(m => m.CollectionBooks)
            .HasForeignKey(m => m.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Book)
            .WithMany(m => m.CollectionBooks)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.CollectionId, m.BookId }).IsUnique();
    }
}