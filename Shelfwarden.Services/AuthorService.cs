namespace Shelfwarden.Services;

public class AuthorService(IRepository<Author> authorRepository) : IAuthorService
{
    public async Task<Result<IReadOnlyList<AuthorDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Author>
        {
            PageNumber = 1,
            PageSize = Math.Clamp(limit, 1, 200),
            OrderBy = q => q.OrderBy(a => a.NormalizedName),
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = a => EF.Functions.Like(a.NormalizedName, $"%{needle}%");
        }

        var authors = await authorRepository.FindAsync(options);
        IReadOnlyList<AuthorDto> result = authors
            .Select(a => new AuthorDto(a.Id, a.Name, a.Biography))
            .ToList();
        return Result.Success(result);
    }

    public async Task<Result<AuthorDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == id,
        });
        if (author is null)
        {
            return Result.NotFound();
        }
        return Result.Success(new AuthorDto(author.Id, author.Name, author.Biography));
    }

    public async Task<Result<AuthorDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Invalid(new ValidationError(nameof(name), "Name is required."));
        }

        string trimmed = name.Trim();
        string normalised = trimmed.ToLowerInvariant();

        var existing = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.NormalizedName == normalised,
        });
        if (existing is not null)
        {
            return Result.Success(new AuthorDto(existing.Id, existing.Name, existing.Biography));
        }

        var created = await authorRepository.InsertAsync(new Author
        {
            Name = trimmed,
            NormalizedName = normalised,
        });
        return Result.Success(new AuthorDto(created.Id, created.Name, created.Biography));
    }
}
