namespace Shelfwarden.Services;

public class TagService(
    IUserContextService userContext,
    IRepository<Tag> tagRepository,
    IRepository<BookTag> bookTagRepository) : ITagService
{
    public async Task<Result<IReadOnlyList<TagDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Tag>
        {
            OrderBy = q => q.OrderBy(t => t.NormalizedName),
            CancellationToken = cancellationToken,
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = t => EF.Functions.Like(t.NormalizedName, $"%{needle}%");
        }

        var rows = (await tagRepository.FindAsync(options)).ToList();

        IReadOnlyList<TagDto> list = rows.Select(t => new TagDto(t.Id, t.Name)).ToList();
        return Result.Success(list);
    }

    public async Task<Result<TagDto>> CreateAsync(string name, CancellationToken cancellationToken = default)
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

        var existing = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = t => t.NormalizedName == normalised,
            CancellationToken = cancellationToken,
        });
        if (existing is not null)
        {
            return Result.Conflict($"A tag named \"{trimmed}\" already exists.");
        }

        var created = await tagRepository.InsertAsync(new Tag
        {
            Name = trimmed,
            NormalizedName = normalised,
        });

        return Result.Success(new TagDto(created.Id, created.Name));
    }

    public async Task<Result<TagDto>> UpdateAsync(int id, string name, CancellationToken cancellationToken = default)
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

        var entity = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = t => t.Id == id,
            CancellationToken = cancellationToken,
        });
        if (entity is null)
        {
            return Result.NotFound();
        }

        var clash = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = t => t.NormalizedName == normalised && t.Id != id,
            CancellationToken = cancellationToken,
        });
        if (clash is not null)
        {
            return Result.Conflict($"A tag named \"{trimmed}\" already exists.");
        }

        entity.Name = trimmed;
        entity.NormalizedName = normalised;
        var updated = await tagRepository.UpdateAsync(entity);
        return Result.Success(new TagDto(updated.Id, updated.Name));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var entity = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = t => t.Id == id,
            CancellationToken = cancellationToken,
        });
        if (entity is null)
        {
            return Result.NotFound();
        }

        var joins = await bookTagRepository.FindAsync(new SearchOptions<BookTag>
        {
            Query = bt => bt.TagId == id,
            CancellationToken = cancellationToken,
        });
        if (joins.Any())
        {
            await bookTagRepository.DeleteAsync(joins);
        }

        await tagRepository.DeleteAsync(entity);
        return Result.Success();
    }

    public async Task<Result> MergeAsync(int targetTagId, IReadOnlyCollection<int> sourceTagIds, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (sourceTagIds.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(sourceTagIds), "Select at least one source tag."));
        }

        var sourceIds = sourceTagIds
            .Where(id => id > 0 && id != targetTagId)
            .Distinct()
            .ToList();
        if (sourceIds.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(sourceTagIds), "Select at least one source tag different from the target."));
        }

        var target = await tagRepository.FindOneAsync(new SearchOptions<Tag>
        {
            Query = t => t.Id == targetTagId,
            CancellationToken = cancellationToken,
        });
        if (target is null)
        {
            return Result.NotFound($"Target tag {targetTagId} was not found.");
        }

        var joins = (await bookTagRepository.FindAsync(new SearchOptions<BookTag>
        {
            Query = bt => sourceIds.Contains(bt.TagId) || bt.TagId == targetTagId,
            CancellationToken = cancellationToken,
        })).ToList();

        var targetBookIds = joins
            .Where(j => j.TagId == targetTagId)
            .Select(j => j.BookId)
            .ToHashSet();

        var sourceJoins = joins.Where(j => sourceIds.Contains(j.TagId)).ToList();
        foreach (var join in sourceJoins)
        {
            if (targetBookIds.Contains(join.BookId))
            {
                await bookTagRepository.DeleteAsync(join);
                continue;
            }

            join.TagId = targetTagId;
            await bookTagRepository.UpdateAsync(join);
            targetBookIds.Add(join.BookId);
        }

        var sources = await tagRepository.FindAsync(new SearchOptions<Tag>
        {
            Query = t => sourceIds.Contains(t.Id),
            CancellationToken = cancellationToken,
        });
        if (sources.Any())
        {
            await tagRepository.DeleteAsync(sources);
        }

        return Result.Success();
    }
}