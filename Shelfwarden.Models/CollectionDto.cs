namespace Shelfwarden.Models;

/// <summary>
/// A user-curated themed grouping of books. <see cref="IsGlobal"/> is <see langword="true"/> when
/// <paramref name="OwnerUserId"/> is <see cref="Shelfwarden.Constants.GlobalUserId"/>.
/// </summary>
/// <param name="Id">Collection id.</param>
/// <param name="Name">Collection name.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="OwnerUserId">Owning user, or <see cref="Shelfwarden.Constants.GlobalUserId"/> for a global collection.</param>
/// <param name="IsGlobal"><see langword="true"/> when every user can see it.</param>
/// <param name="BookCount">Books in the collection.</param>
/// <param name="CreatedAt">When it was created, in UTC.</param>
/// <param name="Banner">Tile image for list views.</param>
public record CollectionDto(
    int Id,
    string Name,
    string? Description,
    string OwnerUserId,
    bool IsGlobal,
    int BookCount,
    DateTime CreatedAt,
    CardBannerPreview Banner);

/// <summary>Detail projection that also embeds the books inside the collection.</summary>
/// <param name="Id">Collection id.</param>
/// <param name="Name">Collection name.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="OwnerUserId">Owning user, or <see cref="Shelfwarden.Constants.GlobalUserId"/> for a global collection.</param>
/// <param name="IsGlobal"><see langword="true"/> when every user can see it.</param>
/// <param name="CreatedAt">When it was created, in UTC.</param>
/// <param name="Books">Books in the collection.</param>
/// <param name="BannerSettings">Banner choices for the edit form.</param>
public record CollectionDetailDto(
    int Id,
    string Name,
    string? Description,
    string OwnerUserId,
    bool IsGlobal,
    DateTime CreatedAt,
    IReadOnlyList<BookListItemDto> Books,
    CardBannerSettingsDto BannerSettings);

public record CreateCollectionRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Admins can mark a collection as global so it shows up for every user. Ignored for
    /// non-administrator callers — they always get a personal collection.
    /// </summary>
    public bool IsGlobal { get; init; }
}

public record UpdateCollectionRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// When true, the collection is stored under the global owner. Changing this flag
    /// requires an administrator; personal owners may only keep <c>false</c>.
    /// </summary>
    public bool IsGlobal { get; init; }

    /// <summary>
    /// <see cref="CardHeaderBannerMode.UploadedImage"/> is rejected when no banner file is stored.
    /// Switching away from an uploaded image deletes that file.
    /// </summary>
    public CardHeaderBannerMode CardBannerMode { get; init; } = CardHeaderBannerMode.RandomCovers;

    /// <summary>
    /// Used when <see cref="CardBannerMode"/> is <see cref="CardHeaderBannerMode.SelectedBooks"/>.
    /// Only books in this collection are kept, at most <see cref="CardBannerLimits.MaxStripCovers"/>, and the update is rejected when none remain.
    /// </summary>
    public IReadOnlyList<int> CardBannerSelectedBookIds { get; init; } = [];
}