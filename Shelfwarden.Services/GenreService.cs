namespace Shelfwarden.Services;

public class GenreService(IRepository<Genre> genreRepository) : IGenreService
{
    public async Task<Result<IReadOnlyList<GenreDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Genre>
        {
            PageNumber = 1,
            PageSize = Math.Clamp(limit, 1, 200),
            OrderBy = q => q.OrderBy(g => g.NormalizedName),
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = g => EF.Functions.Like(g.NormalizedName, $"%{needle}%");
        }

        var genres = await genreRepository.FindAsync(options);
        IReadOnlyList<GenreDto> result = genres
            .Select(g => new GenreDto(g.Id, g.Name))
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<GenreDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var genre = await genreRepository.FindOneAsync(new SearchOptions<Genre>
        {
            Query = g => g.Id == id,
        });

        return genre is null ? (Result<GenreDto>)Result.NotFound() : Result.Success(new GenreDto(genre.Id, genre.Name));
    }

    public async Task<Result<GenreDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Invalid(new ValidationError(nameof(name), "Name is required."));
        }

        string trimmed = name.Trim();
        string normalised = trimmed.ToLowerInvariant();

        var existing = await genreRepository.FindOneAsync(new SearchOptions<Genre>
        {
            Query = g => g.NormalizedName == normalised,
        });

        if (existing is not null)
        {
            return Result.Success(new GenreDto(existing.Id, existing.Name));
        }

        var created = await genreRepository.InsertAsync(new Genre
        {
            Name = trimmed,
            NormalizedName = normalised,
        });

        return Result.Success(new GenreDto(created.Id, created.Name));
    }
}