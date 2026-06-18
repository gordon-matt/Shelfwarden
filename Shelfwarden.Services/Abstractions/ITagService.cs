namespace Shelfwarden.Services;

public interface ITagService
{
    /// <summary>Returns every tag in the library, ordered by normalised name, for filter dropdowns.</summary>
    Task<Result<IReadOnlyList<TagDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    Task<Result<TagDto>> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<Result<TagDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Bulk-deletes the supplied tags (and their book links) in one round-trip.</summary>
    /// <returns>Number of tags actually removed.</returns>
    Task<Result<int>> DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Deletes tags that are not assigned to any book.</summary>
    /// <returns>Number of tags actually removed.</returns>
    Task<Result<int>> DeleteUnusedAsync(CancellationToken cancellationToken = default);

    Task<Result> MergeAsync(int targetTagId, IReadOnlyCollection<int> sourceTagIds, CancellationToken cancellationToken = default);
}