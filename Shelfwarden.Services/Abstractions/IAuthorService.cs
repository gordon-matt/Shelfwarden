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

    /// <summary>
    /// Searches OpenLibrary authors for a user-provided name and returns candidate matches.
    /// </summary>
    Task<Result<IReadOnlyList<OpenLibraryAuthorMatchDto>>> SearchOpenLibraryAuthorsAsync(string query, int limit = 8, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports bio/photo metadata for an existing author from a selected OpenLibrary author id.
    /// </summary>
    Task<Result<AuthorOpenLibraryImportResultDto>> ImportFromOpenLibraryAsync(int authorId, string openLibraryAuthorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually updates author display name, biography, and optionally uploads/replaces the author photo.
    /// </summary>
    /// <param name="displayName">When non-null, replaces the stored name (must not match another author).</param>
    Task<Result<AuthorProfileUpdateResultDto>> UpdateProfileAsync(
        int authorId,
        string? biography,
        byte[]? photoBytes,
        string? photoExtension,
        string? displayName = null,
        CancellationToken cancellationToken = default);
}