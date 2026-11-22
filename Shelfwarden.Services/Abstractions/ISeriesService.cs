namespace Shelfwarden.Services;

/// <summary>
/// Named groups of books. A name matches without case, and a leading "A", "An", or "The" is
/// ignored, so those do not create a second series.
/// </summary>
public interface ISeriesService
{
    /// <summary>
    /// Series whose name contains <paramref name="query"/>, ignoring case, up to
    /// <paramref name="limit"/> (clamped between 1 and 200), with a book count.
    /// </summary>
    Task<Result<IReadOnlyList<SeriesDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>The series, including its universe when it has one.</summary>
    Task<Result<SeriesDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns the series with this name, or creates it. See the class summary for how names match.</summary>
    Task<Result<SeriesDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full /series index with covers for collage rendering. Empty series (zero books) are
    /// included so admins can still see and clean them up if they want.
    /// </summary>
    Task<Result<IReadOnlyList<SeriesListItemDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a series. Administrator only.
    /// </summary>
    Task<Result<SeriesDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the series row and clears series assignment on all books (books are not deleted).
    /// Administrator only.
    /// </summary>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}