namespace Shelfwarden.Services;

/// <summary>
/// User-scoped bookmarks for ebooks. Each call uses the calling user's identity from
/// <see cref="IUserContextService"/> — bookmarks are never shared across users so the
/// service guarantees you can only see / modify your own.
/// </summary>
public interface IBookmarkService
{
    /// <summary>Returns the calling user's bookmarks for a given book, ordered oldest-first.</summary>
    Task<Result<IReadOnlyList<BookmarkDto>>> ListAsync(int bookId, CancellationToken cancellationToken = default);

    Task<Result<BookmarkDto>> CreateAsync(int bookId, CreateBookmarkRequest request, CancellationToken cancellationToken = default);

    Task<Result<BookmarkDto>> UpdateAsync(int id, UpdateBookmarkRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
