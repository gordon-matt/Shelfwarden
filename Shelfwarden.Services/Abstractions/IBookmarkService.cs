namespace Shelfwarden.Services;

/// <summary>
/// User-scoped bookmarks for ebooks. Each call uses the calling user's identity from
/// <see cref="IUserContextService"/> — bookmarks are never shared across users so the
/// service guarantees you can only see / modify your own.
/// </summary>
public interface IBookmarkService
{
    /// <summary>Returns the calling user's bookmarks for a given book, ordered oldest-first.</summary>
    /// <param name="bookId">Book id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>That user's bookmarks, oldest first. Unauthorized when nobody is signed in.</returns>
    Task<Result<IReadOnlyList<BookmarkDto>>> ListAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a bookmark for the calling user. Either a page number or a location is required.
    /// </summary>
    /// <param name="bookId">Book id.</param>
    /// <param name="request">The bookmark. A page number or a location is required. A blank title or note is stored as <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The saved bookmark, or invalid, unauthorized, or not found.</returns>
    Task<Result<BookmarkDto>> CreateAsync(int bookId, CreateBookmarkRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the title and note on the calling user's bookmark. A blank title or note is cleared.
    /// The page and location stay as they are. Returns unauthorized when nobody is signed in.
    /// A missing bookmark is not found, and another user's bookmark is forbidden.
    /// </summary>
    /// <param name="id">Bookmark id.</param>
    /// <param name="request">The new title and note. The page and location are not on this request.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The updated bookmark, or unauthorized, not found, or forbidden.</returns>
    Task<Result<BookmarkDto>> UpdateAsync(int id, UpdateBookmarkRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the calling user's bookmark. Returns unauthorized when nobody is signed in.
    /// A missing bookmark is not found, and another user's bookmark is forbidden.
    /// </summary>
    /// <param name="id">Bookmark id.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>Success, or unauthorized, not found, or forbidden.</returns>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
