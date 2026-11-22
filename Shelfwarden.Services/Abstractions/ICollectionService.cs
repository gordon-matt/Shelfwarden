namespace Shelfwarden.Services;

/// <summary>
/// User-curated themed groupings of books. Two flavours:
/// <list type="bullet">
///   <item><b>Personal</b>: <see cref="Collection.OwnerUserId"/> = the calling user. Only that user can see / edit.</item>
///   <item><b>Global</b>: <see cref="Collection.OwnerUserId"/> = <see cref="Constants.GlobalUserId"/>. Visible to everyone, but only administrators can create or modify.</item>
/// </list>
/// </summary>
public interface ICollectionService
{
    /// <summary>Returns the calling user's personal collections plus all global collections.</summary>
    /// <param name="shelfId">When set, only collections that contain at least one book on this shelf are returned.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Personal collections for the caller, plus every global collection.</returns>
    Task<Result<IReadOnlyList<CollectionDto>>> ListAsync(int? shelfId = null, CancellationToken cancellationToken = default);

    /// <summary>Returns a single collection with its books, after verifying the caller can see it.</summary>
    /// <param name="id">Collection id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The collection, or not found when the caller cannot see it.</returns>
    Task<Result<CollectionDetailDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a collection for the calling user. An administrator who asks for a global collection
    /// creates one owned by <see cref="Constants.GlobalUserId"/> instead. A second collection with
    /// the same name for that owner is refused.
    /// </summary>
    /// <param name="request">Name, description, and whether it is global.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The new collection, or unauthorized or conflict.</returns>
    Task<Result<CollectionDto>> CreateAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the collection and replaces its description and banner. The owner can change a
    /// personal collection. An administrator can change a global one, and can move a collection
    /// between personal and global.
    /// </summary>
    /// <param name="id">Collection id.</param>
    /// <param name="request">New name, description, global flag, and banner.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The updated collection, or unauthorized, not found, forbidden, or invalid.</returns>
    Task<Result<CollectionDto>> UpdateAsync(int id, UpdateCollectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes the collection and its membership. The books stay. The card banner file is removed.</summary>
    /// <param name="id">Collection id.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>Success, or unauthorized, not found, or forbidden.</returns>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds a book. A book that is already in the collection is left as it is.</summary>
    /// <param name="collectionId">Collection id.</param>
    /// <param name="bookId">Book to add.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, or unauthorized, not found, or forbidden.</returns>
    Task<Result> AddBookAsync(int collectionId, int bookId, CancellationToken cancellationToken = default);

    /// <summary>Bulk variant of <see cref="AddBookAsync"/>. Books already in the collection are skipped silently.</summary>
    /// <param name="collectionId">Collection id.</param>
    /// <param name="bookIds">Books to add. Ones already in the collection are skipped.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Number of brand-new associations created.</returns>
    Task<Result<int>> AddBooksAsync(int collectionId, IReadOnlyCollection<int> bookIds, CancellationToken cancellationToken = default);

    /// <summary>Adds every book in a series, in series order. Duplicates are skipped silently.</summary>
    /// <param name="collectionId">Collection id.</param>
    /// <param name="seriesId">Series whose books to add.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Number of brand-new associations created.</returns>
    Task<Result<int>> AddSeriesAsync(int collectionId, int seriesId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the book from the collection. The book stays. A missing collection, or a book that is
    /// not in it, is not found. The owner can change a personal collection. An administrator can change
    /// a global one. Anyone else is forbidden. Returns unauthorized when nobody is signed in.
    /// </summary>
    /// <param name="collectionId">Collection id.</param>
    /// <param name="bookId">Book to remove.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, or unauthorized, not found, or forbidden.</returns>
    Task<Result> RemoveBookAsync(int collectionId, int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an uploaded banner and switches the collection to that image. The owner can change a
    /// personal collection. An administrator can change a global one. Anyone else is forbidden.
    /// Accepts JPG, PNG, GIF, or WebP, up to 2 MB. A missing collection is not found. Returns
    /// unauthorized when nobody is signed in.
    /// </summary>
    /// <param name="collectionId">Collection id.</param>
    /// <param name="content">The image.</param>
    /// <param name="fileName">Original file name. The extension must be JPG, PNG, GIF, or WebP.</param>
    /// <param name="contentLength">Length in bytes, when the caller knows it. Over 2 MB is rejected.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, or unauthorized, not found, forbidden, or invalid.</returns>
    Task<Result> UploadCardBannerAsync(
        int collectionId,
        Stream content,
        string fileName,
        long? contentLength,
        CancellationToken cancellationToken = default);
}