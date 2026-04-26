namespace Shelfwarden.Services;

public interface IAuthorService
{
    Task<Result<IReadOnlyList<AuthorDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default);

    Task<Result<AuthorDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<AuthorDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default);
}
