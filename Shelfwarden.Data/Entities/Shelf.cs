namespace Shelfwarden.Data.Entities;

public class Shelf : BaseEntity<int>, ICardBannerOwner
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>UTC timestamp of the most recent successful scan, or null when never scanned.</summary>
    public DateTime? LastScannedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>How this shelf's folders are laid out on disk. Set at creation, immutable afterwards.</summary>
    public DirectoryStructure DirectoryStructure { get; set; } = DirectoryStructure.Unstructured;

    /// <summary>When true, the scanner uses each file's name (without extension) as the book title, ignoring metadata titles.</summary>
    public bool AlwaysUseFileNameForTitle { get; set; }

    /// <summary>When true, author metadata is dropped during scanning.</summary>
    public bool AlwaysIgnoreAuthor { get; set; }

    /// <summary>When true, tag metadata is dropped during scanning.</summary>
    public bool AlwaysIgnoreTags { get; set; }

    /// <summary>When true, genre metadata is dropped during scanning.</summary>
    public bool AlwaysIgnoreGenres { get; set; }

    /// <summary>When true, newly-discovered books are added to the <see cref="NewBooksCollectionName"/> collection.</summary>
    public bool AssignNewBooksToCollection { get; set; }

    /// <summary>Name of the (global) collection that new books are filed into when <see cref="AssignNewBooksToCollection"/> is true.</summary>
    public string? NewBooksCollectionName { get; set; }

    /// <summary>
    /// When true, the scanner queries online metadata sources (Google Books, Open Library) during
    /// import and back-fills any fields the local file/sidecar metadata left empty.
    /// </summary>
    public bool AutoFetchOnlineMetadata { get; set; }

    public CardHeaderBannerMode CardBannerMode { get; set; }

    public string? CardBannerImageFileName { get; set; }

    public string? CardBannerBookIdsJson { get; set; }

    public virtual ICollection<ShelfFolder> Folders { get; set; } = [];

    public virtual ICollection<Book> Books { get; set; } = [];

    /// <summary>When non-empty (together with <see cref="RoleAccessEntries"/>), only matching users/roles may view this shelf.</summary>
    public virtual ICollection<ShelfUserAccess> UserAccessEntries { get; set; } = [];

    public virtual ICollection<ShelfRoleAccess> RoleAccessEntries { get; set; } = [];
}

public class ShelfMap : IEntityTypeConfiguration<Shelf>
{
    public void Configure(EntityTypeBuilder<Shelf> builder)
    {
        builder.ToTable("Shelves", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Description).HasMaxLength(2048).IsUnicode(true);
        builder.Property(m => m.CardBannerImageFileName).HasMaxLength(256);
        builder.Property(m => m.CardBannerBookIdsJson).HasMaxLength(512);
        builder.Property(m => m.NewBooksCollectionName).HasMaxLength(256).IsUnicode(true);

        builder.HasIndex(m => m.Name).IsUnique();
    }
}