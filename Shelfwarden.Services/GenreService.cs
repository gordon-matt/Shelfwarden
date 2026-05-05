namespace Shelfwarden.Services;

public class GenreService(
    IUserContextService userContext,
    IRepository<Genre> genreRepository,
    IRepository<BookGenre> bookGenreRepository) : IGenreService
{
    public async Task<Result<IReadOnlyList<GenreDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Genre>
        {
            OrderBy = q => q.OrderBy(g => g.NormalizedName),
            CancellationToken = cancellationToken,
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = g => EF.Functions.Like(g.NormalizedName, $"%{needle}%");
        }

        var rows = (await genreRepository.FindAsync(options)).ToList();
        IReadOnlyList<GenreDto> result = rows.Select(g => new GenreDto(g.Id, g.Name)).ToList();
        return Result.Success(result);
    }

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

    public async Task<Result<GenreDto>> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Invalid(new ValidationError(nameof(name), "Name is required."));
        }

        string trimmed = name.Trim();
        string normalised = trimmed.ToLowerInvariant();

        var existing = await genreRepository.FindOneAsync(new SearchOptions<Genre>
        {
            Query = g => g.NormalizedName == normalised,
            CancellationToken = cancellationToken,
        });
        if (existing is not null)
        {
            return Result.Conflict($"A genre named \"{trimmed}\" already exists.");
        }

        var created = await genreRepository.InsertAsync(new Genre
        {
            Name = trimmed,
            NormalizedName = normalised,
        });

        return Result.Success(new GenreDto(created.Id, created.Name));
    }

    public async Task<Result<GenreDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Invalid(new ValidationError(nameof(name), "Name is required."));
        }

        string trimmed = name.Trim();
        string normalised = trimmed.ToLowerInvariant();

        var entity = await genreRepository.FindOneAsync(new SearchOptions<Genre>
        {
            Query = g => g.Id == id,
            CancellationToken = cancellationToken,
        });
        if (entity is null)
        {
            return Result.NotFound();
        }

        var clash = await genreRepository.FindOneAsync(new SearchOptions<Genre>
        {
            Query = g => g.NormalizedName == normalised && g.Id != id,
            CancellationToken = cancellationToken,
        });
        if (clash is not null)
        {
            return Result.Conflict($"A genre named \"{trimmed}\" already exists.");
        }

        entity.Name = trimmed;
        entity.NormalizedName = normalised;
        var updated = await genreRepository.UpdateAsync(entity);
        return Result.Success(new GenreDto(updated.Id, updated.Name));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var entity = await genreRepository.FindOneAsync(new SearchOptions<Genre>
        {
            Query = g => g.Id == id,
            CancellationToken = cancellationToken,
        });
        if (entity is null)
        {
            return Result.NotFound();
        }

        var joins = await bookGenreRepository.FindAsync(new SearchOptions<BookGenre>
        {
            Query = bg => bg.GenreId == id,
            CancellationToken = cancellationToken,
        });
        if (joins.Any())
        {
            await bookGenreRepository.DeleteAsync(joins);
        }

        await genreRepository.DeleteAsync(entity);
        return Result.Success();
    }

    public async Task<Result> MergeAsync(int targetGenreId, IReadOnlyCollection<int> sourceGenreIds, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (sourceGenreIds.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(sourceGenreIds), "Select at least one source genre."));
        }

        var sourceIds = sourceGenreIds
            .Where(id => id > 0 && id != targetGenreId)
            .Distinct()
            .ToList();
        if (sourceIds.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(sourceGenreIds), "Select at least one source genre different from the target."));
        }

        var target = await genreRepository.FindOneAsync(new SearchOptions<Genre>
        {
            Query = g => g.Id == targetGenreId,
            CancellationToken = cancellationToken,
        });
        if (target is null)
        {
            return Result.NotFound($"Target genre {targetGenreId} was not found.");
        }

        var joins = (await bookGenreRepository.FindAsync(new SearchOptions<BookGenre>
        {
            Query = bg => sourceIds.Contains(bg.GenreId) || bg.GenreId == targetGenreId,
            CancellationToken = cancellationToken,
        })).ToList();

        var targetBookIds = joins
            .Where(j => j.GenreId == targetGenreId)
            .Select(j => j.BookId)
            .ToHashSet();

        var sourceJoins = joins.Where(j => sourceIds.Contains(j.GenreId)).ToList();
        foreach (var join in sourceJoins)
        {
            if (targetBookIds.Contains(join.BookId))
            {
                await bookGenreRepository.DeleteAsync(join);
                continue;
            }

            join.GenreId = targetGenreId;
            await bookGenreRepository.UpdateAsync(join);
            targetBookIds.Add(join.BookId);
        }

        var sources = await genreRepository.FindAsync(new SearchOptions<Genre>
        {
            Query = g => sourceIds.Contains(g.Id),
            CancellationToken = cancellationToken,
        });
        if (sources.Any())
        {
            await genreRepository.DeleteAsync(sources);
        }

        return Result.Success();
    }
}