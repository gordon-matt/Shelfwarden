using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

/// <summary>
/// A root folder that belongs to a <see cref="Library"/>. Books may live at any depth under this
/// folder (the scanner walks recursively). A library can have multiple roots.
/// </summary>
public class LibraryFolder : BaseEntity<int>
{
    public required string Path { get; set; }

    public int LibraryId { get; set; }

    public virtual Library Library { get; set; } = null!;
}

public class LibraryFolderMap : IEntityTypeConfiguration<LibraryFolder>
{
    public void Configure(EntityTypeBuilder<LibraryFolder> builder)
    {
        builder.ToTable("LibraryFolders", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Path).IsRequired().HasMaxLength(1024).IsUnicode(true);

        builder.HasOne(m => m.Library)
            .WithMany(m => m.Folders)
            .HasForeignKey(m => m.LibraryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Non-unique on purpose: Path can be up to 1024 unicode chars which exceeds the
        // composite-key length limit on SQL Server / MySQL InnoDB. The service layer
        // enforces uniqueness within a library before insert.
        builder.HasIndex(m => m.LibraryId);
    }
}
