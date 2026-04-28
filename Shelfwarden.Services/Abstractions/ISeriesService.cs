namespace Shelfwarden.Services;

public interface ISeriesService
{
    Task<Result<IReadOnlyList<SeriesDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default);

    Task<Result<SeriesDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<SeriesDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full /series index with covers for collage rendering. Empty series (zero books) are
    /// included so admins can still see and clean them up if they want.
    /// </summary>
    Task<Result<IReadOnlyList<SeriesListItemDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);
}