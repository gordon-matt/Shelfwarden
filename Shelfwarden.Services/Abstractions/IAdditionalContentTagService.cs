namespace Shelfwarden.Services;

public interface IAdditionalContentTagService
{
    Task<Result<IReadOnlyList<AdditionalContentTagDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    Task<Result<AdditionalContentTagDto>> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<Result<AdditionalContentTagDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<int>> DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    Task<Result> MergeAsync(int targetTagId, IReadOnlyCollection<int> sourceTagIds, CancellationToken cancellationToken = default);
}
