using Hangfire;
using Microsoft.EntityFrameworkCore;
using Shelfwarden.Data.Entities;
using Shelfwarden.Services.Scanning;

namespace Shelfwarden.Services;

public class ShelfService(
    ILogger<ShelfService> logger,
    IUserContextService userContext,
    IBackgroundJobClient backgroundJobs,
    IRepository<Shelf> shelfRepository,
    IRepository<ShelfFolder> folderRepository,
    IRepository<ShelfUserAccess> shelfUserAccessRepository,
    IRepository<ShelfRoleAccess> shelfRoleAccessRepository,
    IRepository<Book> bookRepository) : IShelfService
{
    public async Task<Result<IReadOnlyList<ShelfDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var shelves = await shelfRepository.FindAsync(new SearchOptions<Shelf>
        {
            Include = q => q
                .Include(x => x.Folders)
                .Include(x => x.UserAccessEntries)
                .Include(x => x.RoleAccessEntries),
            OrderBy = q => q.OrderBy(x => x.Name),
            CancellationToken = cancellationToken,
        });

        var shelfList = shelves.ToList();
        if (!userContext.IsAdministrator())
        {
            shelfList = shelfList.Where(s => ShelfAccessEvaluator.CanAccessShelf(s, userContext)).ToList();
        }

        var bookCounts = await bookRepository.FindAsync(
            new SearchOptions<Book>(),
            b => new { b.ShelfId });

        var counts = bookCounts
            .GroupBy(x => x.ShelfId)
            .ToDictionary(g => g.Key, g => g.Count());

        IReadOnlyList<ShelfDto> result = shelfList
            .Select(s => MapShelf(s, counts.GetValueOrDefault(s.Id, 0)))
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<ShelfDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        Shelf? shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = x => x.Id == id,
            Include = q => q
                .Include(x => x.Folders)
                .Include(x => x.UserAccessEntries)
                .Include(x => x.RoleAccessEntries),
            CancellationToken = cancellationToken,
        });

        if (shelf is null)
        {
            return Result.NotFound($"Shelf {id} not found.");
        }

        if (!userContext.IsAdministrator() && !ShelfAccessEvaluator.CanAccessShelf(shelf, userContext))
        {
            return Result.NotFound($"Shelf {id} not found.");
        }

        int bookCount = await bookRepository.CountAsync(b => b.ShelfId == id);

        return Result.Success(MapShelf(shelf, bookCount));
    }

    public async Task<Result<ShelfDto>> CreateAsync(CreateShelfRequest request, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var folderPaths = NormalizeFolders(request.Folders);
        if (folderPaths.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(request.Folders), "At least one folder is required."));
        }

        var existing = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = s => s.Name == request.Name,
            CancellationToken = cancellationToken,
        });
        if (existing is not null)
        {
            return Result.Conflict($"A shelf named '{request.Name}' already exists.");
        }

        var shelf = await shelfRepository.InsertAsync(new Shelf
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
        });

        var folders = folderPaths
            .Select(p => new ShelfFolder { ShelfId = shelf.Id, Path = p })
            .ToList();
        await folderRepository.InsertAsync(folders);

        await SyncShelfAccessAsync(shelf.Id, request.AllowedUserIds, request.AllowedRoleNames, cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Created shelf {ShelfId} '{Name}' with {FolderCount} folder(s)",
                shelf.Id, shelf.Name, folders.Count);
        }

        return await GetByIdAsync(shelf.Id, cancellationToken);
    }

    public async Task<Result<ShelfDto>> UpdateAsync(int id, UpdateShelfRequest request, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = x => x.Id == id,
            Include = q => q.Include(x => x.Folders),
            CancellationToken = cancellationToken,
        });
        if (shelf is null)
        {
            return Result.NotFound($"Shelf {id} not found.");
        }

        var nameClash = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = s => s.Id != id && s.Name == request.Name,
            CancellationToken = cancellationToken,
        });
        if (nameClash is not null)
        {
            return Result.Conflict($"Another shelf is already named '{request.Name}'.");
        }

        shelf.Name = request.Name.Trim();
        shelf.Description = request.Description?.Trim();
        await shelfRepository.UpdateAsync(shelf);

        var desiredFolders = NormalizeFolders(request.Folders);
        var existingFolders = shelf.Folders.ToList();

        var toRemove = existingFolders
            .Where(f => !desiredFolders.Contains(f.Path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (toRemove.Count > 0)
        {
            await folderRepository.DeleteAsync(toRemove);
        }

        var existingPaths = existingFolders.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toAdd = desiredFolders
            .Where(p => !existingPaths.Contains(p))
            .Select(p => new ShelfFolder { ShelfId = id, Path = p })
            .ToList();
        if (toAdd.Count > 0)
        {
            await folderRepository.InsertAsync(toAdd);
        }

        await SyncShelfAccessAsync(id, request.AllowedUserIds, request.AllowedRoleNames, cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = x => x.Id == id,
            CancellationToken = cancellationToken,
        });

        if (shelf is null)
        {
            return Result.NotFound();
        }

        // FK ON DELETE CASCADE handles folders + books + access rows.
        await shelfRepository.DeleteAsync(shelf);
        return Result.Success();
    }

    public async Task<Result> ScheduleScanAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = x => x.Id == id,
            CancellationToken = cancellationToken,
        });
        if (shelf is null)
        {
            return Result.NotFound();
        }

        string jobId = backgroundJobs.Enqueue<IScannerService>(s => s.ScanShelfAsync(id, CancellationToken.None));

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Enqueued scan for shelf {ShelfId} as Hangfire job {JobId}", id, jobId);
        }

        return Result.Success();
    }

    private async Task SyncShelfAccessAsync(
        int shelfId,
        IReadOnlyList<string>? userIds,
        IReadOnlyList<string>? roleNames,
        CancellationToken cancellationToken)
    {
        List<string> users = (userIds ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<string> roles = (roleNames ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingUsers = await shelfUserAccessRepository.FindAsync(new SearchOptions<ShelfUserAccess>
        {
            Query = e => e.ShelfId == shelfId,
            CancellationToken = cancellationToken,
        });
        if (existingUsers.Count > 0)
        {
            await shelfUserAccessRepository.DeleteAsync(existingUsers);
        }

        if (users.Count > 0)
        {
            await shelfUserAccessRepository.InsertAsync(users
                .Select(uid => new ShelfUserAccess { ShelfId = shelfId, UserId = uid })
                .ToList());
        }

        var existingRoles = await shelfRoleAccessRepository.FindAsync(new SearchOptions<ShelfRoleAccess>
        {
            Query = e => e.ShelfId == shelfId,
            CancellationToken = cancellationToken,
        });
        if (existingRoles.Count > 0)
        {
            await shelfRoleAccessRepository.DeleteAsync(existingRoles);
        }

        if (roles.Count > 0)
        {
            await shelfRoleAccessRepository.InsertAsync(roles
                .Select(rn => new ShelfRoleAccess { ShelfId = shelfId, NormalizedRoleName = rn })
                .ToList());
        }
    }

    private static List<string> NormalizeFolders(IReadOnlyList<string> folders)
        => [.. folders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().TrimEnd('/', '\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static ShelfDto MapShelf(Shelf shelf, int bookCount)
    {
        var allowedUsers = (shelf.UserAccessEntries ?? [])
            .OrderBy(u => u.UserId)
            .Select(u => u.UserId)
            .ToList();

        var allowedRoles = (shelf.RoleAccessEntries ?? [])
            .OrderBy(r => r.NormalizedRoleName)
            .Select(r => r.NormalizedRoleName)
            .ToList();

        return new ShelfDto(
            shelf.Id,
            shelf.Name,
            shelf.Description,
            shelf.LastScannedAt,
            shelf.CreatedAt,
            bookCount,
            shelf.Folders
                .OrderBy(f => f.Path)
                .Select(f => new ShelfFolderDto(f.Id, f.Path))
                .ToList(),
            allowedUsers,
            allowedRoles);
    }
}
