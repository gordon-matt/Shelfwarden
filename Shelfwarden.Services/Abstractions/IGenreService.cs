namespace Shelfwarden.Services;

public interface IGenreService
{
    Task<Result<IReadOnlyList<GenreDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default);

    Task<Result<GenreDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<GenreDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);
}