namespace Shelfwarden.Services;

public interface IAuthorService
{
    Task<Result<IReadOnlyList<AuthorDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default);

    Task<Result<AuthorDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<AuthorDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full /authors index with per-author book counts, ordered alphabetically by
    /// normalised name. Authors with zero books (on the selected shelf, or overall when <paramref name="shelfId"/> is null) are omitted.
    /// </summary>
    Task<Result<IReadOnlyList<AuthorListItemDto>>> ListAsync(string? query = null, int? shelfId = null, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Deletes every author that has no linked books (orphans after book removals).
    /// Intended for scheduled Hangfire maintenance — does not check caller roles.
    /// </summary>
    /// <returns>The number of authors deleted.</returns>
    Task<Result<int>> DeleteAuthorsWithNoBooksAsync(CancellationToken cancellationToken = default);

    /// <summary>Count of books that have no author links (optionally restricted to a shelf).</summary>
    Task<Result<int>> GetBooksWithoutAuthorsCountAsync(int? shelfId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detail view for the synthetic &quot;Unknown&quot; author — books with no linked authors.
    /// Shape matches <see cref="GetDetailAsync"/> (series groups + standalone).
    /// </summary>
    Task<Result<AuthorDetailDto>> GetUnknownAuthorDetailAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every book–author link for the given authors, deletes author photos, then deletes the author rows.
    /// Administrator only.
    /// </summary>
    Task<Result<int>> DeleteAuthorsAsync(IReadOnlyList<int> authorIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves all book links from <paramref name="otherAuthorIds"/> onto <paramref name="primaryAuthorId"/>,
    /// resolves duplicate (book, author) pairs, deletes merged authors and their photos.
    /// Administrator only.
    /// </summary>
    Task<Result> MergeAuthorsAsync(int primaryAuthorId, IReadOnlyList<int> otherAuthorIds, CancellationToken cancellationToken = default);
}