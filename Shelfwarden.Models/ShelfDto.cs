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
}
