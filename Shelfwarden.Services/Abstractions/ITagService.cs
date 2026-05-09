namespace Shelfwarden.Services;

public interface ITagService
{
    /// <summary>Returns every tag in the library, ordered by normalised name, for filter dropdowns.</summary>
    Task<Result<IReadOnlyList<TagDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    Task<Result<TagDto>> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<Result<TagDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> MergeAsync(int targetTagId, IReadOnlyCollection<int> sourceTagIds, CancellationToken cancellationToken = default);
}