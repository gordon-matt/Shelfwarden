namespace Shelfwarden.Services;

/// <summary>
/// Searching, editing, and deleting books, plus the calling user's reading progress.
/// A book on a shelf the caller cannot open is reported as missing. Editing and deleting
/// are administrator-only.
/// </summary>
public interface IBookService
{
    /// <summary>
    /// One page of books the caller can open. The page starts at 1 and holds at most 200 books.
    /// A shelf id limits the page to that shelf. A series, author, genre, collection, or tag id
    /// of -1 keeps books that have none of that relationship. Several tag ids keep books that
    /// have every one of them. Awaiting review
    /// means <see cref="Book.UpdatedAt"/> is still null. Read and unread use
    /// <see cref="Constants.FinishedThresholdPercent"/>. Title search is a case-insensitive
    /// substring, and a single letter matches the title or the sort title. With no signed-in user,
    /// the read-status and rating filters are ignored.
    /// </summary>
    /// <param name="request">Filters, page, and sort. See <see cref="BookSearchRequest"/>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page, or an empty page when the caller cannot open the chosen shelf.</returns>
    Task<Result<PagedList<BookListItemDto>>> SearchAsync(BookSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The book, including the calling user's rating. A missing book and a book on a shelf the
    /// caller cannot open both come back as not found.
    /// </summary>
    /// <param name="id">Book id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The book, or not found.</returns>
    Task<Result<BookDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the supplied book ids to <see cref="BookListItemDto"/> entries. Missing ids
    /// are silently dropped; surviving entries follow the database's natural sort order
    /// (i.e. callers that need a specific order should reorder client-side using a dictionary).
    /// Cheaper than calling <see cref="SearchAsync"/> with a wide page when the caller already
    /// knows exactly which ids it wants.
    /// </summary>
    /// <param name="ids">Book ids. Missing ones are left out.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The books that exist and that the caller can open.</returns>
    Task<Result<IReadOnlyList<BookListItemDto>>> GetListItemsByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the book's bibliographic fields and its authors, genres, and tags. Administrator only.
    /// Sets <see cref="Book.UpdatedAt"/>, which takes the book out of the awaiting-review filter.
    /// The file on disk is left as it is.
    /// </summary>
    /// <param name="id">Book id.</param>
    /// <param name="request">The new bibliographic fields. The file path and size are not on this request.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The updated book, or forbidden or not found.</returns>
    Task<Result<BookDto>> UpdateAsync(int id, UpdateBookRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes the book row. Administrator only. The ebook file is left on disk.</summary>
    /// <param name="id">Book id.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Success, or forbidden or not found.</returns>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Stores the calling user's place in the book, replacing any progress they already have.</summary>
    /// <param name="id">Book id.</param>
    /// <param name="request">The new place. <see cref="SaveProgressRequest.PageNumber"/> is a PDF page and <see cref="SaveProgressRequest.Location"/> is an EPUB position.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>The saved progress, or unauthorized or not found.</returns>
    Task<Result<BookProgressDto>> SaveProgressAsync(int id, SaveProgressRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns the calling user's saved reading progress for the given book, or null if none.</summary>
    /// <param name="id">Book id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The progress, or <see langword="null"/> inside a successful result when none is stored.</returns>
    Task<Result<BookProgressDto?>> GetProgressAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the book as read for the calling user by pinning their progress to 100%. Used by
    /// the BookCard context menu so users can flip the "finished" state without opening the reader.
    /// </summary>
    /// <param name="id">Book id.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>The saved progress, or unauthorized or not found.</returns>
    Task<Result<BookProgressDto>> MarkAsReadAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the book as unread for the calling user by deleting any existing progress record.
    /// This makes the book disappear from "Continue Reading" / Finished counts entirely rather
    /// than leaving a 0% row that masquerades as "started".
    /// </summary>
    /// <param name="id">Book id.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Success, or unauthorized or not found.</returns>
    Task<Result> MarkAsUnreadAsync(int id, CancellationToken cancellationToken = default);
}