namespace Shelfwarden.Models;

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
    CardBannerPreview Banner,
    CardBannerSettingsDto? BannerSettings = null);

public record ShelfFolderDto(int Id, string Path);

public record CreateShelfRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    [StringLength(2048)]
    public string? Description { get; init; }

    [MinLength(1)]
    public required IReadOnlyList<string> Folders { get; init; }

    /// <summary>Specific user ids that may view this shelf when access is restricted.</summary>
    public IReadOnlyList<string> AllowedUserIds { get; init; } = [];

    /// <summary>Role names (e.g. User, Administrator) that may view this shelf when access is restricted.</summary>
    public IReadOnlyList<string> AllowedRoleNames { get; init; } = [];

    /// <summary>Folder layout convention. Chosen at creation and immutable afterwards.</summary>
    public DirectoryStructure DirectoryStructure { get; init; } = DirectoryStructure.Unstructured;

    public bool AlwaysUseFileNameForTitle { get; init; }

    public bool AlwaysIgnoreAuthor { get; init; }

    public bool AlwaysIgnoreTags { get; init; }

    public bool AlwaysIgnoreGenres { get; init; }

    public bool AssignNewBooksToCollection { get; init; }

    [StringLength(256)]
    public string? NewBooksCollectionName { get; init; }
}

public record UpdateShelfRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    [StringLength(2048)]
    public string? Description { get; init; }

    public IReadOnlyList<string> Folders { get; init; } = [];

    public IReadOnlyList<string> AllowedUserIds { get; init; } = [];

    public IReadOnlyList<string> AllowedRoleNames { get; init; } = [];

    public CardHeaderBannerMode CardBannerMode { get; init; } = CardHeaderBannerMode.RandomCovers;

    public IReadOnlyList<int> CardBannerSelectedBookIds { get; init; } = [];

    // DirectoryStructure is intentionally absent: it can only be chosen when the shelf is created.

    public bool AlwaysUseFileNameForTitle { get; init; }

    public bool AlwaysIgnoreAuthor { get; init; }

    public bool AlwaysIgnoreTags { get; init; }

    public bool AlwaysIgnoreGenres { get; init; }

    public bool AssignNewBooksToCollection { get; init; }

    [StringLength(256)]
    public string? NewBooksCollectionName { get; init; }
}