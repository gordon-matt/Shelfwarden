namespace Shelfwarden.Services;

public interface IBookService
{
    Task<Result<PagedList<BookListItemDto>>> SearchAsync(BookSearchRequest request, CancellationToken cancellationToken = default);

    Task<Result<BookDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the supplied book ids to <see cref="BookListItemDto"/> entries. Missing ids
    /// are silently dropped; surviving entries follow the database's natural sort order
    /// (i.e. callers that need a specific order should reorder client-side using a dictionary).
    /// Cheaper than calling <see cref="SearchAsync"/> with a wide page when the caller already
    /// knows exactly which ids it wants.
    /// </summary>
    Task<Result<IReadOnlyList<BookListItemDto>>> GetListItemsByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    Task<Result<BookDto>> UpdateAsync(int id, UpdateBookRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<BookProgressDto>> SaveProgressAsync(int id, SaveProgressRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns the calling user's saved reading progress for the given book, or null if none.</summary>
    Task<Result<BookProgressDto?>> GetProgressAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the book as read for the calling user by pinning their progress to 100%. Used by
    /// the BookCard context menu so users can flip the "finished" state without opening the reader.
    /// </summary>
    Task<Result<BookProgressDto>> MarkAsReadAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the book as unread for the calling user by deleting any existing progress record.
    /// This makes the book disappear from "Continue Reading" / Finished counts entirely rather
    /// than leaving a 0% row that masquerades as "started".
    /// </summary>
    Task<Result> MarkAsUnreadAsync(int id, CancellationToken cancellationToken = default);
}