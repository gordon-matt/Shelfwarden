namespace Shelfwarden.Services;

/// <summary>
/// Extra files that are not ebooks. Scanning and registering them, assigning them to authors,
/// books, and series, and reading text for the in-app viewer.
/// </summary>
public interface IAdditionalContentService
{
    /// <summary>
    /// Scans the extras directory, adding DB entries for newly discovered files and removing
    /// entries for files that no longer exist on disk. Does not re-assign authorship.
    /// </summary>
    /// <returns>The count of newly discovered files.</returns>
    Task<Result<int>> ScanExtrasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers existing files at the given absolute paths as extra content without moving or
    /// copying them. Paths already in the library are skipped.
    /// </summary>
    Task<Result<RegisterExternalFilesResult>> RegisterExternalFilesAsync(
        IReadOnlyList<string> absoluteFilePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists extra content with optional author/series filters and server-side paging.
    /// </summary>
    /// <param name="authorFilter"><c>0</c> = any author, <c>-1</c> = unassigned only, otherwise author id.</param>
    /// <param name="seriesFilter"><c>0</c> = any series, <c>-1</c> = not linked to any series, otherwise series id.</param>
    /// <param name="tagId"><c>-1</c> = items with no tags; <c>null</c> or <c>0</c> = any when <paramref name="tagIds"/> is empty.</param>
    /// <param name="tagIds">When non-empty, item must have all listed tag ids (AND).</param>
    Task<Result<PagedList<AdditionalContentItemDto>>> ListPagedAsync(
        int page,
        int pageSize,
        int authorFilter = 0,
        int seriesFilter = 0,
        int? tagId = null,
        IReadOnlyList<int>? tagIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>All extra-content tags (for admin filters and tag pickers).</summary>
    Task<Result<IReadOnlyList<AdditionalContentTagDto>>> ListTagsAsync(
        string? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>Tags used on items assigned to the given author.</summary>
    Task<Result<IReadOnlyList<AdditionalContentTagDto>>> ListTagsForAuthorAsync(
        int authorId,
        string? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces tags on each listed item with <see cref="SetAdditionalContentTagsRequest.TagNames"/>.</summary>
    Task<Result> SetItemsTagsAsync(
        SetAdditionalContentTagsRequest request,
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
    /// Assigns the listed items to the given author. Scanned files are moved under that author's
    /// extras folder, with no series subfolder. Manually imported files stay where they are.
    /// Items already on that author are skipped. When an item moves off a different author, its
    /// book and series links are removed first. A missing author is not found. Unknown item ids
    /// are skipped.
    /// </summary>
    Task<Result> AssignToAuthorAsync(AssignContentToAuthorRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds book links that are not already on the item. Existing links stay. A missing item is not found.
    /// </summary>
    Task<Result> AssociateWithBooksAsync(AssociateContentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds series links that are not already on the item. Existing links stay. A missing item is not found.
    /// When the item was found by a scan, already has an author, and exactly one series id was sent,
    /// the file is moved under that author's extras folder into a folder named for the series.
    /// A manually imported file is not moved.
    /// </summary>
    Task<Result> AssociateWithSeriesAsync(AssociateContentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Renames a content item's display name. Does not rename the file on disk.</summary>
    Task<Result> RenameAsync(RenameContentItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the listed library rows. A scanned file is removed from disk. A manually imported
    /// file is left where it is.
    /// </summary>
    Task<Result> DeleteAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default);

    /// <summary>Returns a single item by id (used for download/serve operations).</summary>
    Task<Result<AdditionalContentItemDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads UTF-8 text from disk for in-app viewing (txt / md / html). Same visibility as
    /// <see cref="GetByIdAsync"/>; avoids HTTP so Blazor Server does not need cookie-forwarding.
    /// </summary>
    Task<Result<string>> GetViewableTextAsync(int id, CancellationToken cancellationToken = default);
}