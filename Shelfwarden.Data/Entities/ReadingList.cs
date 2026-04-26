using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

public class ReadingList : BaseEntity<int>
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public required string OwnerUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<ReadingListItem> Items { get; set; } = [];
}

public class ReadingListMap : IEntityTypeConfiguration<ReadingList>
{
    public void Configure(EntityTypeBuilder<ReadingList> builder)
    {
        builder.ToTable("ReadingLists", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Description).IsUnicode(true);
        builder.Property(m => m.OwnerUserId).IsRequired().HasMaxLength(450);

        builder.HasIndex(m => new { m.OwnerUserId, m.Name }).IsUnique();
    }
}
