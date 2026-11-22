namespace Shelfwarden.Services;

/// <summary>
/// Per-user ordered queues of books to read. Reading lists are always private to their owner —
/// there is no "global" mode like <see cref="Collection"/> has.
/// </summary>
public interface IReadingListService
{
    /// <summary>The calling user's own lists. Universe reading orders are left out.</summary>
    /// <param name="shelfId">When set, only lists that contain at least one book on this shelf are returned.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The caller's personal lists.</returns>
    Task<Result<IReadOnlyList<ReadingListDto>>> ListAsync(int? shelfId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the list and its books in queue order, with the caller's reading progress and the
    /// banner settings. A personal list is visible only to its owner. A universe reading order is
    /// visible to any signed-in user and includes that universe's name. A missing list is not found.
    /// A personal list that belongs to someone else is forbidden. Returns unauthorized when nobody
    /// is signed in. An item whose book is gone is left out.
    /// </summary>
    /// <param name="id">Reading list id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The list, or unauthorized, not found, or forbidden.</returns>
    Task<Result<ReadingListDetailDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a personal reading list for the calling user. Returns unauthorized when nobody is
    /// signed in. A personal list that user already owns with the same name is refused. A blank
    /// description is stored as <see langword="null"/>.
    /// </summary>
    /// <param name="request">Name and description.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The new list, or unauthorized or conflict.</returns>
    Task<Result<ReadingListDto>> CreateAsync(CreateReadingListRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the list and replaces its description and banner. The owner can change a personal
    /// list. An administrator can change a universe reading order. Anyone else is forbidden.
    /// A blank description is cleared. A missing list is not found. Returns unauthorized when
    /// nobody is signed in.
    /// </summary>
    /// <param name="id">Reading list id.</param>
    /// <param name="request">New name, description, and banner.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The updated list, or unauthorized, not found, forbidden, or invalid.</returns>
    /// <remarks>
    /// Choosing selected-book covers keeps only books that are on the list, and at most
    /// <see cref="CardBannerLimits.MaxStripCovers"/> of them. That mode is rejected when none remain.
    /// An uploaded-image banner is rejected when the list has neither a stored banner name nor a
    /// banner file. Switching away from an uploaded image deletes that file.
    /// </remarks>
    Task<Result<ReadingListDto>> UpdateAsync(int id, UpdateReadingListRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the list and its items. The books stay. The card banner file is removed. The owner
    /// can delete a personal list. An administrator can delete a universe reading order. Anyone
    /// else is forbidden. A missing list is not found. Returns unauthorized when nobody is signed in.
    /// </summary>
    /// <param name="id">Reading list id.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>Success, or unauthorized, not found, or forbidden.</returns>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a book at the end of the list. A book that is already on the list stays where it is.
    /// A universe reading order refuses a book that is not in that universe.
    /// </summary>
    /// <param name="readingListId">Reading list id.</param>
    /// <param name="bookId">Book to append.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, or unauthorized, not found, forbidden, or invalid.</returns>
    Task<Result> AddBookAsync(int readingListId, int bookId, CancellationToken cancellationToken = default);

    /// <summary>Bulk variant of <see cref="AddBookAsync"/>. Books already in the list are skipped silently.</summary>
    /// <param name="readingListId">Reading list id.</param>
    /// <param name="bookIds">Books to append. Ones already on the list are skipped.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Number of brand-new entries appended.</returns>
    Task<Result<int>> AddBooksAsync(int readingListId, IReadOnlyCollection<int> bookIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends every book in the series to the end of the list, ordered by <see cref="Book.NumberInSeries"/>
    /// ascending. Books already in the list are skipped silently.
    /// </summary>
    /// <param name="readingListId">Reading list id.</param>
    /// <param name="seriesId">Series whose books to append.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Number of brand-new entries appended.</returns>
    Task<Result<int>> AddSeriesAsync(int readingListId, int seriesId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the book from the list. The book stays. A missing list, or a book that is not on it,
    /// is not found. The owner can change a personal list. An administrator can change a universe
    /// reading order. Anyone else is forbidden. Returns unauthorized when nobody is signed in.
    /// The remaining items are given positions starting at 0, in the order they already had.
    /// </summary>
    /// <param name="readingListId">Reading list id.</param>
    /// <param name="bookId">Book to remove.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, or unauthorized, not found, or forbidden.</returns>
    Task<Result> RemoveBookAsync(int readingListId, int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives the listed item ids positions starting at 0, in the order supplied. Ids that are not
    /// on this list are ignored. Items the caller left out keep the position they already have.
    /// </summary>
    /// <param name="readingListId">Reading list id.</param>
    /// <param name="orderedItemIds">Item ids in the new order. Ids that are not on the list are ignored.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, or unauthorized, not found, or forbidden.</returns>
    Task<Result> ReorderAsync(int readingListId, IReadOnlyList<int> orderedItemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an uploaded banner and switches the list to that image. The owner can change a
    /// personal list. An administrator can change a universe reading order. Anyone else is forbidden.
    /// Accepts JPG, PNG, GIF, or WebP, up to 2 MB. A missing list is not found. Returns unauthorized
    /// when nobody is signed in.
    /// </summary>
    /// <param name="readingListId">Reading list id.</param>
    /// <param name="content">The image.</param>
    /// <param name="fileName">Original file name. The extension must be JPG, PNG, GIF, or WebP.</param>
    /// <param name="contentLength">Length in bytes, when the caller knows it. Over 2 MB is rejected.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, or unauthorized, not found, forbidden, or invalid.</returns>
    Task<Result> UploadCardBannerAsync(
        int readingListId,
        Stream content,
        string fileName,
        long? contentLength,
        CancellationToken cancellationToken = default);
}