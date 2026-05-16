namespace Shelfwarden.Data.Entities;

public class BookAdditionalContentItem : BaseEntity<int>
{
    public int BookId { get; set; }

    public int AdditionalContentItemId { get; set; }

    public virtual Book Book { get; set; } = null!;

    public virtual AdditionalContentItem AdditionalContentItem { get; set; } = null!;
}

public class BookAdditionalContentItemMap : IEntityTypeConfiguration<BookAdditionalContentItem>
{
    public void Configure(EntityTypeBuilder<BookAdditionalContentItem> builder)
    {
        builder.ToTable("BookAdditionalContent", Constants.Schemas.App);
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.Book)
            .WithMany(m => m.BookAdditionalContent)
            .HasForeignKey(m => m.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.AdditionalContentItem)
            .WithMany(m => m.BookAdditionalContents)
            .HasForeignKey(m => m.AdditionalContentItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.BookId, m.AdditionalContentItemId }).IsUnique();
    }
}
