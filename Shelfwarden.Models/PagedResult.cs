namespace Shelfwarden.Models;

/// <summary>Plain paginated list. One page of items and how many exist in total.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The page. Empty when nothing matched.</param>
/// <param name="TotalCount">How many items exist, not just this page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Items requested per page.</param>
public record PagedList<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    /// <summary>How many pages <see cref="TotalCount"/> fills. Zero when <see cref="PageSize"/> is not positive.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record BookSearchRequest
{
    /// <summary>
    /// Restrict results by first title character.
    /// Use "#" for titles that start with a number or symbol.
    /// </summary>
    public string? StartsWith { get; init; }

    /// <summary>Limits the page to this shelf. A shelf the caller cannot open yields an empty page. -1 is not a special value here.</summary>
    public int? ShelfId { get; init; }

    /// <summary>
    /// Series filter. Positive id restricts to that series; <c>-1</c> means books with no series assigned.
    /// </summary>
    public int? SeriesId { get; init; }

    /// <summary>
    /// Author filter. Positive id restricts to books that list that author; <c>-1</c> means books with no authors assigned.
    /// </summary>
    public int? AuthorId { get; init; }

    /// <summary>
    /// Genre filter. Positive id restricts to that genre; <c>-1</c> means books with no genres assigned.
    /// </summary>
    public int? GenreId { get; init; }

    /// <summary>
    /// Collection filter. Positive id restricts to books in that collection; <c>-1</c> means books not in any collection.
    /// </summary>
    public int? CollectionId { get; init; }

    /// <summary>When set, only books that appear in this reading list are returned.</summary>
    public int? ReadingListId { get; init; }

    /// <summary>When set, only books on this universe's timeline are returned.</summary>
    public int? UniverseId { get; init; }

    /// <summary>
    /// Tag filter. Positive id restricts to books that have that tag; <c>-1</c> means books with no tags.
    /// </summary>
    public int? TagId { get; init; }

    /// <summary>
    /// Multi-tag filter. When provided, books must contain all selected tags (logical AND).
    /// </summary>
    public IReadOnlyList<int> TagIds { get; init; } = [];

    /// <summary>
    /// When true, only include books that have never had metadata edited by a user
    /// (<c>UpdatedAt is null</c>).
    /// </summary>
    public bool AwaitingReview { get; init; }

    /// <summary>
    /// Filter by the calling user's reading state. <see cref="BookReadStatusFilter.Any"/> is the
    /// default and applies no filter; <see cref="BookReadStatusFilter.Read"/> keeps books whose
    /// progress for this user is at or above <see cref="Shelfwarden.Constants.FinishedThresholdPercent"/>;
    /// <see cref="BookReadStatusFilter.Unread"/> keeps everything else (including books with no
    /// progress row at all).
    /// </summary>
    public BookReadStatusFilter ReadStatus { get; init; } = BookReadStatusFilter.Any;

    /// <summary>
    /// Filter by the calling user's own star rating. When set (1–5), only books this user has rated at
    /// or above the value are returned. Null applies no filter.
    /// </summary>
    public int? MinRating { get; init; }

    /// <summary>Case-insensitive substring matched against the title only. A blank value applies no title filter.</summary>
    public string? Query { get; init; }

    /// <summary>Page number. Values below 1 are read as page 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Books per page. Values are clamped to the range 1 through 200.</summary>
    public int PageSize { get; init; } = 24;

    /// <summary>How the page is ordered. <see cref="BookSortBy.LastReadAt"/> is not applied.</summary>
    public BookSortBy SortBy { get; init; } = BookSortBy.Title;

    /// <summary>When <see langword="true"/>, the chosen sort is reversed. <see cref="BookSortBy.LastReadAt"/> ignores this.</summary>
    public bool SortDescending { get; init; }
}

public enum BookSortBy
{
    /// <summary>Sort title when one is stored, otherwise the title.</summary>
    Title = 0,

    /// <summary>When the book was added.</summary>
    AddedAt = 1,

    /// <summary>Publication date.</summary>
    PublishedOn = 2,

    /// <summary>Not applied. Search orders these the same way as <see cref="Title"/>, ascending.</summary>
    LastReadAt = 3,

    /// <summary>Series, then position in that series.</summary>
    NumberInSeries = 4,

    /// <summary>When the book row was last edited.</summary>
    UpdatedAt = 5,
}

public enum BookReadStatusFilter
{
    /// <summary>No read-status filter.</summary>
    Any = 0,

    /// <summary>The caller has reached <see cref="Shelfwarden.Constants.FinishedThresholdPercent"/>.</summary>
    Read = 1,

    /// <summary>The caller has not reached that threshold, including books with no progress row.</summary>
    Unread = 2,
}