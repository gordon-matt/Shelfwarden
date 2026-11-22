namespace Shelfwarden.Models;

/// <summary>
/// An ordered queue of books a user wants to read. A personal list has no global mode.
/// A universe reading order is a separate list, owned by <see cref="Shelfwarden.Constants.GlobalUserId"/>, and is not one of these.
/// </summary>
/// <param name="Id">Reading list id.</param>
/// <param name="Name">List name.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="OwnerUserId">The user who owns the list.</param>
/// <param name="BookCount">Books on the list.</param>
/// <param name="CreatedAt">When it was created, in UTC.</param>
/// <param name="Banner">Tile image for list views.</param>
public record ReadingListDto(
    int Id,
    string Name,
    string? Description,
    string OwnerUserId,
    int BookCount,
    DateTime CreatedAt,
    CardBannerPreview Banner);

/// <summary>
/// A reading list and its books, in queue order. <see cref="UniverseId"/> is set for a universe
/// reading order and <see langword="null"/> for a personal list. <paramref name="UniverseName"/> follows it.
/// </summary>
/// <param name="Id">Reading list id.</param>
/// <param name="Name">List name.</param>
/// <param name="Description">Description, or <see langword="null"/>.</param>
/// <param name="OwnerUserId">The owner. A universe reading order uses <see cref="Shelfwarden.Constants.GlobalUserId"/>.</param>
/// <param name="CreatedAt">When it was created, in UTC.</param>
/// <param name="Items">Books in queue order.</param>
/// <param name="BannerSettings">Banner choices for the edit form.</param>
/// <param name="UniverseId">Universe this reading order belongs to, or <see langword="null"/> for a personal list.</param>
/// <param name="UniverseName">That universe's name, or <see langword="null"/> when <paramref name="UniverseId"/> is.</param>
public record ReadingListDetailDto(
    int Id,
    string Name,
    string? Description,
    string OwnerUserId,
    DateTime CreatedAt,
    IReadOnlyList<ReadingListEntryDto> Items,
    CardBannerSettingsDto BannerSettings,
    int? UniverseId = null,
    string? UniverseName = null);

/// <summary>Single entry inside a reading list. Holds the position so reorder UIs have something to bind to.</summary>
/// <param name="Id">Item id.</param>
/// <param name="Position">Zero-based place in the queue.</param>
/// <param name="Book">The book.</param>
public record ReadingListEntryDto(
    int Id,
    int Position,
    BookListItemDto Book);

/// <summary>
/// A personal reading list for the signed-in user. A blank description is stored as <see langword="null"/>.
/// Another of the caller's personal lists with the same name is refused.
/// </summary>
public record CreateReadingListRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }
}

public record UpdateReadingListRequest
{
    [Required, StringLength(256)]
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// <see cref="CardHeaderBannerMode.UploadedImage"/> is rejected when no banner file is stored.
    /// Switching away from an uploaded image deletes that file.
    /// </summary>
    public CardHeaderBannerMode CardBannerMode { get; init; } = CardHeaderBannerMode.RandomCovers;

    /// <summary>
    /// Used when <see cref="CardBannerMode"/> is <see cref="CardHeaderBannerMode.SelectedBooks"/>.
    /// Only books on this list are kept, at most <see cref="CardBannerLimits.MaxStripCovers"/>, and the update is rejected when none remain.
    /// </summary>
    public IReadOnlyList<int> CardBannerSelectedBookIds { get; init; } = [];
}