namespace Shelfwarden.Services;

public class TagService(IRepository<Tag> tagRepository) : ITagService
{
    public async Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = (await tagRepository.FindAsync(new SearchOptions<Tag>
        {
            OrderBy = q => q.OrderBy(t => t.NormalizedName),
            CancellationToken = cancellationToken,
        })).ToList();

        IReadOnlyList<TagDto> list = rows.Select(t => new TagDto(t.Id, t.Name)).ToList();
        return Result.Success(list);
    }
}
