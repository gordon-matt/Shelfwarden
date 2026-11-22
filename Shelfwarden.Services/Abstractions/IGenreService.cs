namespace Shelfwarden.Services;

/// <summary>
/// Shared genre names. A name matches without case, and two genres cannot share one.
/// Creating, renaming, deleting, and merging are administrator-only.
/// </summary>
public interface IGenreService
{
    /// <summary>Every genre, or those whose name contains <paramref name="query"/>, ignoring case.</summary>
    /// <param name="query">Text the name must contain. Blank returns every genre.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The genres, ordered by normalized name.</returns>
    Task<Result<IReadOnlyList<GenreDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns genres whose name contains <paramref name="query"/>, ignoring case, up to
    /// <paramref name="limit"/> (clamped between 1 and 200), ordered by normalized name.
    /// A blank query returns every genre up to that limit.
    /// </summary>
    /// <param name="query">Text the name must contain. Blank returns every genre up to <paramref name="limit"/>.</param>
    /// <param name="limit">How many to return. Values outside 1 through 200 are clamped.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>The genres, ordered by normalized name.</returns>
    Task<Result<IReadOnlyList<GenreDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Returns the genre, or not found when no genre has this id.</summary>
    /// <param name="id">Genre id.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>The genre, or not found.</returns>
    Task<Result<GenreDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the genre with this name, ignoring case, or creates it. Not limited to administrators.
    /// </summary>
    /// <param name="name">Genre name. Blank is rejected.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>The existing or new genre, or invalid.</returns>
    Task<Result<GenreDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a genre. Administrator only. A name that already exists, ignoring case, is refused.
    /// </summary>
    /// <param name="name">Genre name. Blank is rejected.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The new genre, or invalid, forbidden, or conflict.</returns>
    Task<Result<GenreDto>> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a genre. Administrator only. A blank name is rejected. A name that already exists,
    /// ignoring case, is refused. A missing genre is not found.
    /// </summary>
    /// <param name="id">Genre id.</param>
    /// <param name="name">New name. Blank is rejected.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The renamed genre, or invalid, forbidden, not found, or conflict.</returns>
    Task<Result<GenreDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the genre and its book links. Administrator only. The books stay. A missing genre
    /// is not found.
    /// </summary>
    /// <param name="id">Genre id.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>Success, or forbidden or not found.</returns>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Bulk-deletes the supplied genres (and their book links) in one round-trip.</summary>
    /// <param name="ids">Genre ids. Non-positive ids are ignored.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Number of genres actually removed.</returns>
    Task<Result<int>> DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Deletes genres that are not assigned to any book.</summary>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Number of genres actually removed.</returns>
    Task<Result<int>> DeleteUnusedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Points every book that has a source genre at the target, drops duplicate links, and deletes
    /// the source genres. Administrator only. The target itself is not a source.
    /// </summary>
    /// <param name="targetGenreId">Genre that should keep the books.</param>
    /// <param name="sourceGenreIds">Genres to fold in and delete. The target id is ignored. At least one other id is required.</param>
    /// <param name="cancellationToken">Cancels the merge.</param>
    /// <returns>Success, or invalid, forbidden, or not found.</returns>
    Task<Result> MergeAsync(int targetGenreId, IReadOnlyCollection<int> sourceGenreIds, CancellationToken cancellationToken = default);
}
