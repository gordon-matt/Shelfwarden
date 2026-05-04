using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

public class ReadingListItem : BaseEntity<int>
{
    public int ReadingListId { get; set; }

    public int BookId { get; set; }

    public int Position { get; set; }

    public virtual ReadingList ReadingList { get; set; } = null!;

    public virtual Book Book { get; set; } = null!;
}

public class ReadingListItemMap : IEntityTypeConfiguration<ReadingListItem>
{
    public void Configure(EntityTypeBuilder<ReadingListItem> builder)
    {
        builder.ToTable("ReadingListItems", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.ReadingList)
            .WithMany(m => m.Items)
            .HasForeignKey(m => m.ReadingListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Book)
            .WithMany(b => b.ReadingListItems)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.ReadingListId, m.BookId }).IsUnique();
        builder.HasIndex(m => new { m.ReadingListId, m.Position });
    }
}