namespace Shelfwarden.Services;

/// <summary>
/// Shared book projection helpers. Internal so we can keep the projection in one place
/// without exposing it as part of the public service surface area — every service that
/// returns <see cref="BookListItemDto"/> goes through here so the shape stays consistent.
/// </summary>
internal static class BookProjections
{
    /// <summary>
    /// Builds a <see cref="BookListItemDto"/> from a fully-hydrated <see cref="Book"/>.
    /// The caller is responsible for `.Include`-ing <c>Series</c> + <c>BookAuthors.Author</c>.
    /// </summary>
    public static BookListItemDto ToListItem(Book b, double progressPercent, int? rating = null) => new(
        b.Id,
        b.Title,
        b.Subtitle,
        PrimaryAuthor: b.BookAuthors
            .OrderBy(ba => ba.Position)
            .Select(ba => ba.Author.Name)
            .FirstOrDefault() ?? string.Empty,
        SeriesName: b.Series?.Name,
        NumberInSeries: b.NumberInSeries,
        CoverImagePath: b.CoverImagePath,
        FileFormat: b.FileFormat,
        ProgressPercentage: progressPercent,
        SortTitle: b.SortTitle,
        Description: b.Description,
        Rating: rating);

    public static async Task<Dictionary<int, double>> LoadProgressPercentagesAsync(
        IRepository<BookProgress> progressRepository,
        string? userId,
        IReadOnlyList<int> bookIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId) || bookIds.Count == 0)
        {
            return [];
        }

        var rows = await progressRepository.FindAsync(new SearchOptions<BookProgress>
        {
            Query = p => p.UserId == userId && bookIds.Contains(p.BookId),
            CancellationToken = cancellationToken,
        });

        return rows.ToDictionary(p => p.BookId, p => p.Percentage);
    }
}