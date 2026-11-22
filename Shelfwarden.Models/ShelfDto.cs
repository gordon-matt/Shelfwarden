namespace Shelfwarden.Models;

/// <summary>
/// A library root the caller can open: its folders, who may see it, how a scan treats files, and the card banner.
/// <see cref="DirectoryStructure"/> is chosen when the shelf is created and is not changed later.
/// When <see cref="AllowedUserIds"/> and <see cref="AllowedRoleNames"/> are both empty, every signed-in user may open it.
/// <see cref="AlwaysUseFileNameForTitle"/> makes a scan use each file name, without the extension, as the title.
/// <see cref="AlwaysIgnoreAuthor"/>, <see cref="AlwaysIgnoreTags"/>, and <see cref="AlwaysIgnoreGenres"/> drop that metadata during a scan.
/// <see cref="AssignNewBooksToCollection"/> files newly found books into <see cref="NewBooksCollectionName"/>.
/// <see cref="AutoFetchOnlineMetadata"/> fills empty fields from online sources during a scan.
/// </summary>
/// <param name="Id">Shelf id.</param>
/// <param name="Name">Shelf name.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="LastScannedAt">When the last successful scan finished, or <see langword="null"/> when the shelf has never been scanned.</param>
/// <param name="CreatedAt">When the shelf was created, in UTC.</param>
/// <param name="BookCount">Books on the shelf.</param>
/// <param name="Folders">Folders the scanner walks.</param>
/// <param name="AllowedUserIds">User ids that may open it. Empty, together with <paramref name="AllowedRoleNames"/>, means every signed-in user.</param>
/// <param name="AllowedRoleNames">Role names that may open it. Empty, together with <paramref name="AllowedUserIds"/>, means every signed-in user.</param>
/// <param name="DirectoryStructure">Folder layout. Chosen at creation and not changed later.</param>
/// <param name="AlwaysUseFileNameForTitle"><see langword="true"/> makes a scan use each file name, without the extension, as the title.</param>
/// <param name="AlwaysIgnoreAuthor"><see langword="true"/> drops author metadata during a scan.</param>
/// <param name="AlwaysIgnoreTags"><see langword="true"/> drops tag metadata during a scan.</param>
/// <param name="AlwaysIgnoreGenres"><see langword="true"/> drops genre metadata during a scan.</param>
/// <param name="AssignNewBooksToCollection"><see langword="true"/> files newly found books into <paramref name="NewBooksCollectionName"/>.</param>
/// <param name="NewBooksCollectionName">That collection's name, or <see langword="null"/> when the flag is off.</param>
/// <param name="AutoFetchOnlineMetadata"><see langword="true"/> fills empty fields from online sources during a scan.</param>
/// <param name="Banner">Tile image for list views.</param>
/// <param name="BannerSettings">Edit-form banner settings, or <see langword="null"/> when the caller only needs the preview.</param>
public record ShelfDto(
    int Id,
    string Name,
    string? Description,
    DateTime? LastScannedAt,
    DateTime CreatedAt,
    int BookCount,
    IReadOnlyList<ShelfFolderDto> Folders,
    IReadOnlyList<string> AllowedUserIds,
    IReadOnlyList<string> AllowedRoleNames,
    DirectoryStructure DirectoryStructure,
    bool AlwaysUseFileNameForTitle,
    bool AlwaysIgnoreAuthor,
    bool AlwaysIgnoreTags,
    bool AlwaysIgnoreGenres,
    bool AssignNewBooksToCollection,
    string? NewBooksCollectionName,
    bool AutoFetchOnlineMetadata,
    CardBannerPreview Banner,
    CardBannerSettingsDto? BannerSettings = null);

/// <summary>One folder a shelf scans.</summary>
/// <param name="Id">Folder row id.</param>
/// <param name="Path">Folder path the scanner walks. Trailing slashes are removed when it is saved.</param>
public record ShelfFolderDto(int Id, string Path);

public record CreateShelfRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    [StringLength(2048)]
    public string? Description { get; init; }

    [MinLength(1)]
    public required IReadOnlyList<string> Folders { get; init; }

    /// <summary>
    /// User ids that may open this shelf. When this and <see cref="AllowedRoleNames"/> are both empty, every signed-in user may open it.
    /// </summary>
    public IReadOnlyList<string> AllowedUserIds { get; init; } = [];

    /// <summary>
    /// Role names that may open this shelf. When this and <see cref="AllowedUserIds"/> are both empty, every signed-in user may open it.
    /// </summary>
    public IReadOnlyList<string> AllowedRoleNames { get; init; } = [];

    /// <summary>Folder layout convention. Chosen at creation and immutable afterwards.</summary>
    public DirectoryStructure DirectoryStructure { get; init; } = DirectoryStructure.Unstructured;

    /// <summary>When <see langword="true"/>, a scan uses each file name, without the extension, as the title.</summary>
    public bool AlwaysUseFileNameForTitle { get; init; }

    /// <summary>When <see langword="true"/>, a scan drops author metadata.</summary>
    public bool AlwaysIgnoreAuthor { get; init; }

    /// <summary>When <see langword="true"/>, a scan drops tag metadata.</summary>
    public bool AlwaysIgnoreTags { get; init; }

    /// <summary>When <see langword="true"/>, a scan drops genre metadata.</summary>
    public bool AlwaysIgnoreGenres { get; init; }

    /// <summary>When <see langword="true"/>, newly found books are filed into <see cref="NewBooksCollectionName"/>.</summary>
    public bool AssignNewBooksToCollection { get; init; }

    /// <summary>
    /// Global collection that receives newly found books. Blank becomes <see cref="Shelfwarden.Constants.DefaultNewBooksCollectionName"/>.
    /// The name is not stored when <see cref="AssignNewBooksToCollection"/> is off.
    /// </summary>
    [StringLength(256)]
    public string? NewBooksCollectionName { get; init; }

    /// <summary>When <see langword="true"/>, a scan fills empty fields from online sources.</summary>
    public bool AutoFetchOnlineMetadata { get; init; }
}

public record UpdateShelfRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    [StringLength(2048)]
    public string? Description { get; init; }

    public IReadOnlyList<string> Folders { get; init; } = [];

    /// <summary>
    /// Replaces who may open this shelf. When this and <see cref="AllowedRoleNames"/> are both empty, every signed-in user may open it.
    /// </summary>
    public IReadOnlyList<string> AllowedUserIds { get; init; } = [];

    /// <summary>
    /// Replaces which roles may open this shelf. When this and <see cref="AllowedUserIds"/> are both empty, every signed-in user may open it.
    /// </summary>
    public IReadOnlyList<string> AllowedRoleNames { get; init; } = [];

    /// <summary>
    /// <see cref="CardHeaderBannerMode.UploadedImage"/> is rejected when no banner file is stored.
    /// Switching away from an uploaded image deletes that file.
    /// </summary>
    public CardHeaderBannerMode CardBannerMode { get; init; } = CardHeaderBannerMode.RandomCovers;

    /// <summary>
    /// Used when <see cref="CardBannerMode"/> is <see cref="CardHeaderBannerMode.SelectedBooks"/>.
    /// Only books on this shelf are kept, at most <see cref="CardBannerLimits.MaxStripCovers"/>, and the update is rejected when none remain.
    /// </summary>
    public IReadOnlyList<int> CardBannerSelectedBookIds { get; init; } = [];

    // DirectoryStructure is intentionally absent: it can only be chosen when the shelf is created.

    /// <summary>When <see langword="true"/>, a scan uses each file name, without the extension, as the title.</summary>
    public bool AlwaysUseFileNameForTitle { get; init; }

    /// <summary>When <see langword="true"/>, a scan drops author metadata.</summary>
    public bool AlwaysIgnoreAuthor { get; init; }

    /// <summary>When <see langword="true"/>, a scan drops tag metadata.</summary>
    public bool AlwaysIgnoreTags { get; init; }

    /// <summary>When <see langword="true"/>, a scan drops genre metadata.</summary>
    public bool AlwaysIgnoreGenres { get; init; }

    /// <summary>When <see langword="true"/>, newly found books are filed into <see cref="NewBooksCollectionName"/>.</summary>
    public bool AssignNewBooksToCollection { get; init; }

    /// <summary>
    /// Global collection that receives newly found books. Blank becomes <see cref="Shelfwarden.Constants.DefaultNewBooksCollectionName"/>.
    /// The name is not stored when <see cref="AssignNewBooksToCollection"/> is off.
    /// </summary>
    [StringLength(256)]
    public string? NewBooksCollectionName { get; init; }

    /// <summary>When <see langword="true"/>, a scan fills empty fields from online sources.</summary>
    public bool AutoFetchOnlineMetadata { get; init; }
}