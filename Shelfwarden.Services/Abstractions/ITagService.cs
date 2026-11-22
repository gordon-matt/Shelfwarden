namespace Shelfwarden.Services;

/// <summary>
/// Book tags shared by the whole library. A name matches without case, and two tags cannot share one.
/// Creating, renaming, deleting, and merging are administrator-only.
/// </summary>
public interface ITagService
{
    /// <summary>
    /// Returns every tag, or those whose name contains <paramref name="query"/>, ignoring case,
    /// ordered by normalized name.
    /// </summary>
    /// <param name="query">Text the name must contain. Blank returns every tag.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The tags, ordered by normalized name.</returns>
    Task<Result<IReadOnlyList<TagDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tag. Administrator only. A name that already exists, ignoring case, is refused.
    /// </summary>
    /// <param name="name">Tag name. Blank is rejected.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The new tag, or invalid, forbidden, or conflict.</returns>
    Task<Result<TagDto>> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a tag. Administrator only. A blank name is rejected. A name that already exists,
    /// ignoring case, is refused. A missing tag is not found.
    /// </summary>
    /// <param name="id">Tag id.</param>
    /// <param name="name">New name. Blank is rejected.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The renamed tag, or invalid, forbidden, not found, or conflict.</returns>
    Task<Result<TagDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the tag and its book links. Administrator only. The books stay. A missing tag is not found.
    /// </summary>
    /// <param name="id">Tag id.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>Success, or forbidden or not found.</returns>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Bulk-deletes the supplied tags (and their book links) in one round-trip.</summary>
    /// <param name="ids">Tag ids. Non-positive ids are ignored.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Number of tags actually removed.</returns>
    Task<Result<int>> DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Deletes tags that are not assigned to any book.</summary>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>Number of tags actually removed.</returns>
    Task<Result<int>> DeleteUnusedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Points every book that has a source tag at the target, drops duplicate links, and deletes
    /// the source tags. Administrator only. The target itself is not a source.
    /// </summary>
    /// <param name="targetTagId">Tag that should keep the books.</param>
    /// <param name="sourceTagIds">Tags to fold in and delete. The target id is ignored. At least one other id is required.</param>
    /// <param name="cancellationToken">Cancels the merge.</param>
    /// <returns>Success, or invalid, forbidden, or not found.</returns>
    Task<Result> MergeAsync(int targetTagId, IReadOnlyCollection<int> sourceTagIds, CancellationToken cancellationToken = default);
}
