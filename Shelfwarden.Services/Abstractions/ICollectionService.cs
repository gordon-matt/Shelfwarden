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
    Task<Result<IReadOnlyList<CollectionDto>>> ListAsync(int? shelfId = null, CancellationToken cancellationToken = default);

    /// <summary>Returns a single collection with its books, after verifying the caller can see it.</summary>
    Task<Result<CollectionDetailDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<CollectionDto>> CreateAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default);

    Task<Result<CollectionDto>> UpdateAsync(int id, UpdateCollectionRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> AddBookAsync(int collectionId, int bookId, CancellationToken cancellationToken = default);

    Task<Result> RemoveBookAsync(int collectionId, int bookId, CancellationToken cancellationToken = default);
}