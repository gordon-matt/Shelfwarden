namespace Shelfwarden.Services;

/// <summary>
/// Per-user ordered queues of books to read. Reading lists are always private to their owner —
/// there is no "global" mode like <see cref="Collection"/> has.
/// </summary>
public interface IReadingListService
{
    Task<Result<IReadOnlyList<ReadingListDto>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<ReadingListDetailDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<ReadingListDto>> CreateAsync(CreateReadingListRequest request, CancellationToken cancellationToken = default);

    Task<Result<ReadingListDto>> UpdateAsync(int id, UpdateReadingListRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Appends a book at the end of the list. Returns Conflict if it's already present.</summary>
    Task<Result> AddBookAsync(int readingListId, int bookId, CancellationToken cancellationToken = default);

    Task<Result> RemoveBookAsync(int readingListId, int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-applies positions so the items match the supplied id order (item-id → new position).
    /// Items not listed are dropped; ids not in the list are ignored. Used by drag-reorder UIs.
    /// </summary>
    Task<Result> ReorderAsync(int readingListId, IReadOnlyList<int> orderedItemIds, CancellationToken cancellationToken = default);
}