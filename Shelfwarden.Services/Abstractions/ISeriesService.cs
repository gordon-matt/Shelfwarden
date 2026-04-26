namespace Shelfwarden.Services;

public interface ISeriesService
{
    Task<Result<IReadOnlyList<SeriesDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default);

    Task<Result<SeriesDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<SeriesDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);
}
