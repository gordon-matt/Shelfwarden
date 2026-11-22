namespace Shelfwarden.Services;

/// <summary>
/// Tags on extra content, separate from book tags. A name matches without case.
/// Creating, renaming, deleting, and merging are administrator-only.
/// </summary>
public interface IAdditionalContentTagService
{
    /// <summary>
    /// Returns every extra content tag, or those whose name contains <paramref name="query"/>,
    /// ignoring case, ordered by normalized name.
    /// </summary>
    Task<Result<IReadOnlyList<AdditionalContentTagDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tag. Administrator only. A name that already exists, ignoring case, is refused.
    /// </summary>
    Task<Result<AdditionalContentTagDto>> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a tag. Administrator only. A blank name is rejected. A name that already exists,
    /// ignoring case, is refused. A missing tag is not found.
    /// </summary>
    Task<Result<AdditionalContentTagDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the tag and its links to extra content items. Administrator only. The items stay.
    /// A missing tag is not found.
    /// </summary>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the supplied tags and their links to extra content items. Administrator only.
    /// An id that is not positive is ignored.
    /// </summary>
    /// <returns>The number of tags actually removed.</returns>
    Task<Result<int>> DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Deletes tags that are not assigned to any extra content item.</summary>
    /// <returns>Number of tags actually removed.</returns>
    Task<Result<int>> DeleteUnusedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Points every extra content item that has a source tag at the target, drops duplicate links,
    /// and deletes the source tags. Administrator only. The target itself is not a source, and at
    /// least one source id other than the target is required. A missing target is not found.
    /// </summary>
    Task<Result> MergeAsync(int targetTagId, IReadOnlyCollection<int> sourceTagIds, CancellationToken cancellationToken = default);
}