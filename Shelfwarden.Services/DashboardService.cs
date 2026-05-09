using Microsoft.EntityFrameworkCore;

namespace Shelfwarden.Services;

/// <summary>
/// Aggregates the data shown on the home dashboard. Pulled into a single service so a future
/// REST/GraphQL client could fetch the same payload without duplicating the projection logic
/// that currently lives in <see cref="BookService.SearchAsync"/>.
/// </summary>
public class DashboardService(
    IUserContextService userContext,
    IBookService bookService,
    IShelfAccessService shelfAccessService,
    IRepository<Shelf> shelfRepository,
    IRepository<Book> bookRepository,
    IRepository<Author> authorRepository,
    IRepository<Series> seriesRepository,
    IRepository<BookProgress> progressRepository) : IDashboardService
{
    private const int ContinueReadingSize = 10;
    private const int RecentlyAddedSize = 20;

    public async Task<Result<DashboardDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();

        IReadOnlySet<int>? accessibleShelves = await shelfAccessService.GetAccessibleShelfIdsAsync(cancellationToken);

        int shelfCount = accessibleShelves is null
            ? await shelfRepository.CountAsync()
            : accessibleShelves.Count;

        int bookCount = accessibleShelves is null
            ? await bookRepository.CountAsync()
            : accessibleShelves.Count == 0
                ? 0
                : await bookRepository.CountAsync(b => accessibleShelves.Contains(b.ShelfId));

        var authorCountTask = authorRepository.CountAsync();
        var seriesCountTask = seriesRepository.CountAsync();
        var finishedCountTask = string.IsNullOrEmpty(userId)
            ? Task.FromResult(0)
            : progressRepository.CountAsync(p => p.UserId == userId && p.Percentage >= Constants.FinishedThresholdPercent);

        await Task.WhenAll(authorCountTask, seriesCountTask, finishedCountTask);

        var stats = new DashboardStatsDto(
            ShelfCount: shelfCount,
            BookCount: bookCount,
            AuthorCount: authorCountTask.Result,
            SeriesCount: seriesCountTask.Result,
            FinishedCount: finishedCountTask.Result);

        // "Recently Added" is universal — newest books server-wide, regardless of who's viewing.
        var recentResult = await bookService.SearchAsync(new BookSearchRequest
        {
            Page = 1,
            PageSize = RecentlyAddedSize,
            SortBy = BookSortBy.AddedAt,
            SortDescending = true,
        }, cancellationToken);

        var recentlyAdded = recentResult.IsSuccess
            ? recentResult.Value.Items
            : [];

        // "Continue Reading" is per-user — in-progress books (< FinishedThreshold) with either
        // a non-zero percentage or a saved EPUB CFI, ordered by most recently read.
        IReadOnlyList<BookListItemDto> continueReading = [];
        if (!string.IsNullOrEmpty(userId))
        {
            var inProgress = await progressRepository.FindAsync(new SearchOptions<BookProgress>
            {
                // EPUB progress may have a saved CFI with Percentage still at 0 if location indexing failed;
                // include those rows so "Continue reading" matches PDF behaviour.
                Query = p => p.UserId == userId
                    && p.Percentage < Constants.FinishedThresholdPercent
                    && (p.Percentage > 0 || (p.Location != null && p.Location != "")),
                OrderBy = q => q.OrderByDescending(p => p.LastReadAt),
                PageNumber = 1,
                PageSize = ContinueReadingSize,
            });

            var bookIds = inProgress.Select(p => p.BookId).ToList();
            if (bookIds.Count > 0)
            {
                var books = await bookRepository.FindAsync(new SearchOptions<Book>
                {
                    Query = b => bookIds.Contains(b.Id),
                    Include = q => q
                        .Include(b => b.Series)
                        .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
                    SplitQuery = true,
                });

                var bookRows = books.ToList();
                if (accessibleShelves is not null)
                {
                    bookRows = bookRows.Where(b => accessibleShelves.Contains(b.ShelfId)).ToList();
                }

                var byId = bookRows.ToDictionary(b => b.Id);
                var progressById = inProgress.ToDictionary(p => p.BookId);

                continueReading = bookIds
                    .Where(byId.ContainsKey)
                    .Select(id => BookProjections.ToListItem(byId[id], progressById[id].Percentage))
                    .ToList();
            }
        }

        return Result.Success(new DashboardDto(stats, continueReading, recentlyAdded));
    }
}