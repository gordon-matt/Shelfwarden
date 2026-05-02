using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

/// <summary>
/// A root folder that belongs to a <see cref="Shelf"/>. Books may live at any depth under this
/// folder (the scanner walks recursively). A shelf can have multiple roots.
/// </summary>
public class ShelfFolder : BaseEntity<int>
{
    public required string Path { get; set; }

    public int ShelfId { get; set; }

    public virtual Shelf Shelf { get; set; } = null!;
}

public class ShelfFolderMap : IEntityTypeConfiguration<ShelfFolder>
{
    public void Configure(EntityTypeBuilder<ShelfFolder> builder)
    {
        builder.ToTable("ShelfFolders", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Path).IsRequired().HasMaxLength(1024).IsUnicode(true);

        builder.HasOne(m => m.Shelf)
            .WithMany(m => m.Folders)
            .HasForeignKey(m => m.ShelfId)
            .OnDelete(DeleteBehavior.Cascade);

        // Non-unique on purpose: Path can be up to 1024 unicode chars which exceeds the
        // composite-key length limit on SQL Server / MySQL InnoDB. The service layer
        // enforces uniqueness within a shelf before insert.
        builder.HasIndex(m => m.ShelfId);
    }
}
