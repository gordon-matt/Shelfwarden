namespace Shelfwarden.Services;

public interface IAuthorService
{
    Task<Result<IReadOnlyList<AuthorDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default);

    Task<Result<AuthorDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<AuthorDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full /authors index with per-author book counts, ordered alphabetically by
    /// normalised name. Authors with zero books are omitted to keep the page focused.
    /// </summary>
    Task<Result<IReadOnlyList<AuthorListItemDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns rich detail for a single author: bio, all series the author has at least one
    /// book in (with covers for the collage), and a list of standalone books not in any series.
    /// </summary>
    Task<Result<AuthorDetailDto>> GetDetailAsync(int id, CancellationToken cancellationToken = default);
}
