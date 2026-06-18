namespace Shelfwarden.Services;

public interface IAdditionalContentTagService
{
    Task<Result<IReadOnlyList<AdditionalContentTagDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    Task<Result<AdditionalContentTagDto>> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<Result<AdditionalContentTagDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<int>> DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Deletes tags that are not assigned to any extra content item.</summary>
    /// <returns>Number of tags actually removed.</returns>
    Task<Result<int>> DeleteUnusedAsync(CancellationToken cancellationToken = default);

    Task<Result> MergeAsync(int targetTagId, IReadOnlyCollection<int> sourceTagIds, CancellationToken cancellationToken = default);
}
