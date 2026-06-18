namespace Shelfwarden.Services;

public interface IGenreService
{
    Task<Result<IReadOnlyList<GenreDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GenreDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default);

    Task<Result<GenreDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<GenreDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);

    Task<Result<GenreDto>> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<Result<GenreDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Bulk-deletes the supplied genres (and their book links) in one round-trip.</summary>
    /// <returns>Number of genres actually removed.</returns>
    Task<Result<int>> DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Deletes genres that are not assigned to any book.</summary>
    /// <returns>Number of genres actually removed.</returns>
    Task<Result<int>> DeleteUnusedAsync(CancellationToken cancellationToken = default);

    Task<Result> MergeAsync(int targetGenreId, IReadOnlyCollection<int> sourceGenreIds, CancellationToken cancellationToken = default);
}