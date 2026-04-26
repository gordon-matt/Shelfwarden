namespace Shelfwarden.Models;

/// <summary>Plain paginated DTO. Distinct from <c>Ardalis.Result.PagedResult&lt;T&gt;</c> which is a Result wrapper.</summary>
public record PagedList<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record BookSearchRequest
{
    public int? LibraryId { get; init; }

    public int? SeriesId { get; init; }

    public int? AuthorId { get; init; }

    public int? GenreId { get; init; }

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
}
