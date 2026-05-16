namespace Shelfwarden.Services;

public interface IAdditionalContentService
{
    /// <summary>
    /// Scans the extras directory, adding DB entries for newly discovered files and removing
    /// entries for files that no longer exist on disk. Does not re-assign authorship.
    /// </summary>
    /// <returns>The count of newly discovered files.</returns>
    Task<Result<int>> ScanExtrasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists extra content with optional author/series filters and server-side paging.
    /// </summary>
    /// <param name="authorFilter"><c>0</c> = any author, <c>-1</c> = unassigned only, otherwise author id.</param>
    /// <param name="seriesFilter"><c>0</c> = any series, <c>-1</c> = not linked to any series, otherwise series id.</param>
    Task<Result<PagedList<AdditionalContentItemDto>>> ListPagedAsync(
        int page,
        int pageSize,
        int authorFilter = 0,
        int seriesFilter = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all items associated with the given author (direct author assignment).</summary>
    Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetForAuthorAsync(int authorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all items associated with the given series (via SeriesAdditionalContent) OR with
    /// any book that belongs to this series (via BookAdditionalContent). Intended for the series
    /// detail page which should aggregate both.
    /// </summary>
    Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetForSeriesAsync(int seriesId, CancellationToken cancellationToken = default);

    /// <summary>Returns all items associated with the specific book.</summary>
    Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetForBookAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns the listed items to the given author, moving files on disk to
    /// <c>_extras/{AuthorName}/</c>. If an item already has a different author, its author is
    /// updated and the file is relocated.
    /// </summary>
    Task<Result> AssignToAuthorAsync(AssignContentToAuthorRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Associates a content item with the listed books (replaces existing book associations).
    /// </summary>
    Task<Result> AssociateWithBooksAsync(AssociateContentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Associates a content item with the listed series (replaces existing series associations)
    /// and moves the file to <c>_extras/{AuthorName}/{SeriesName}/</c> for the first series
    /// when exactly one series is given.
    /// </summary>
    Task<Result> AssociateWithSeriesAsync(AssociateContentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Renames a content item's display name. Does not rename the file on disk.</summary>
    Task<Result> RenameAsync(RenameContentItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes the listed items from the database and from disk.</summary>
    Task<Result> DeleteAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default);

    /// <summary>Returns a single item by id (used for download/serve operations).</summary>
    Task<Result<AdditionalContentItemDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
