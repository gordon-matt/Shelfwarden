namespace Shelfwarden.Services;

public interface ITagService
{
    /// <summary>Returns every tag in the library, ordered by normalised name, for filter dropdowns.</summary>
    Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken = default);
}
