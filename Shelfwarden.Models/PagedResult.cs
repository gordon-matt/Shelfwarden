namespace Shelfwarden.Models;

/// <summary>Plain paginated DTO. Distinct from <c>Ardalis.Result.PagedResult&lt;T&gt;</c> which is a Result wrapper.</summary>
public record PagedList<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record BookSearchRequest
{
    /// <summary>
    /// Restrict results by first title character.
    /// Use "#" for titles that start with a number or symbol.
    /// </summary>
    public string? StartsWith { get; init; }

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
    /// progress for this user is at or above <c>Constants.FinishedThresholdPercent</c>;
    /// <see cref="BookReadStatusFilter.Unread"/> keeps everything else (including books with no
    /// progress row at all).
    /// </summary>
    public BookReadStatusFilter ReadStatus { get; init; } = BookReadStatusFilter.Any;

    /// <summary>
    /// Filter by the calling user's own star rating. When set (1–5), only books this user has rated at
    /// or above the value are returned. Null applies no filter.
    /// </summary>
    public int? MinRating { get; init; }

    public string? Query { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 24;

    public BookSortBy SortBy { get; init; } = BookSortBy.Title;

    public bool SortDescending { get; init; }
}

public enum BookSortBy
{
    Title = 0,
    AddedAt = 1,
    PublishedOn = 2,
    LastReadAt = 3,
    NumberInSeries = 4,
    UpdatedAt = 5,
}

public enum BookReadStatusFilter
{
    Any = 0,
    Read = 1,
    Unread = 2,
}