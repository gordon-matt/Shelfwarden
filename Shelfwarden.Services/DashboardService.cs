namespace Shelfwarden.Services;

/// <summary>
/// Aggregates the data shown on the home dashboard. Pulled into a single service so a future
/// REST/GraphQL client could fetch the same payload without duplicating the projection logic
/// that currently lives in <see cref="BookService.SearchAsync"/>.
/// </summary>
public class DashboardService(
    IUserContextService userContext,
    IBookService bookService,
    IRepository<Shelf> shelfRepository,
    IRepository<Book> bookRepository,
    IRepository<Author> authorRepository,
    IRepository<Series> seriesRepository,
    IRepository<BookProgress> progressRepository) : IDashboardService
{
    private const int CarouselSize = 12;
    private const double FinishedThresholdPercent = 95d;

    public async Task<Result<DashboardDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();

        // Headline counts in parallel — cheap server-side counts, no projections to ship.
        var shelfCountTask = shelfRepository.CountAsync();
        var bookCountTask = bookRepository.CountAsync();
        var authorCountTask = authorRepository.CountAsync();
        var seriesCountTask = seriesRepository.CountAsync();
        var finishedCountTask = string.IsNullOrEmpty(userId)
            ? Task.FromResult(0)
            : progressRepository.CountAsync(p => p.UserId == userId && p.Percentage >= FinishedThresholdPercent);

        await Task.WhenAll(shelfCountTask, bookCountTask, authorCountTask, seriesCountTask, finishedCountTask);

        var stats = new DashboardStatsDto(
            ShelfCount: shelfCountTask.Result,
            BookCount: bookCountTask.Result,
            AuthorCount: authorCountTask.Result,
            SeriesCount: seriesCountTask.Result,
            FinishedCount: finishedCountTask.Result);

        // "Recently Added" is universal — newest books server-wide, regardless of who's viewing.
        var recentResult = await bookService.SearchAsync(new BookSearchRequest
        {
            Page = 1,
            PageSize = CarouselSize,
            SortBy = BookSortBy.AddedAt,
            SortDescending = true,
        }, cancellationToken);

        var recentlyAdded = recentResult.IsSuccess
            ? recentResult.Value.Items
            : [];

        // "Continue Reading" is per-user — books with progress > 0 and < FinishedThreshold,
        // ordered by most recently read.
        IReadOnlyList<BookListItemDto> continueReading = [];
        if (!string.IsNullOrEmpty(userId))
        {
            var inProgress = await progressRepository.FindAsync(new SearchOptions<BookProgress>
            {
                Query = p => p.UserId == userId
                    && p.Percentage > 0
                    && p.Percentage < FinishedThresholdPercent,
                OrderBy = q => q.OrderByDescending(p => p.LastReadAt),
                PageNumber = 1,
                PageSize = CarouselSize,
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

                var byId = books.ToDictionary(b => b.Id);
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