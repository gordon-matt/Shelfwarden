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
            OrderBy = query => query.OrderBy(g => g.NormalizedName),
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
            OrderBy = query => query.OrderBy(g => g.NormalizedName),
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

        // Partition source rows: any source-genre row whose BookId is already linked to the
        // target is a duplicate (delete); the rest get re-pointed to the target. Doing this in
        // bulk avoids one round-trip per join row.
        var sourceJoins = joins.Where(j => sourceIds.Contains(j.GenreId)).ToList();
        var toDelete = new List<BookGenre>();
        var toRepoint = new List<BookGenre>();
        foreach (var join in sourceJoins)
        {
            if (!targetBookIds.Add(join.BookId))
            {
                toDelete.Add(join);
                continue;
            }

            join.GenreId = targetGenreId;
            toRepoint.Add(join);
        }

        if (toDelete.Count > 0)
        {
            await bookGenreRepository.DeleteAsync(toDelete);
        }

        if (toRepoint.Count > 0)
        {
            await bookGenreRepository.UpdateAsync(toRepoint);
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

    public async Task<Result<int>> DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var distinctIds = ids.Where(i => i > 0).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return Result.Success(0);
        }

        var entities = (await genreRepository.FindAsync(new SearchOptions<Genre>
        {
            Query = g => distinctIds.Contains(g.Id),
            CancellationToken = cancellationToken,
        })).ToList();

        if (entities.Count == 0)
        {
            return Result.Success(0);
        }

        // Cascade is configured in the entity map but EF still wants the joins removed when
        // we use a hard delete on the principal — drop them explicitly so the operation
        // succeeds across providers regardless of cascade configuration.
        var joinIds = entities.Select(g => g.Id).ToList();
        await bookGenreRepository.DeleteAsync(bg => joinIds.Contains(bg.GenreId));
        await genreRepository.DeleteAsync(entities);

        return Result.Success(entities.Count);
    }

    public async Task<Result<int>> DeleteUnusedAsync(CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var unused = (await genreRepository.FindAsync(new SearchOptions<Genre>
        {
            Query = g => !g.BookGenres.Any(),
            CancellationToken = cancellationToken,
        })).ToList();

        if (unused.Count == 0)
        {
            return Result.Success(0);
        }

        await genreRepository.DeleteAsync(unused);
        return Result.Success(unused.Count);
    }
}