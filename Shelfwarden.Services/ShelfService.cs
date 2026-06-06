using Hangfire;
using Shelfwarden.Services.Scanning;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services;

public class ShelfService(
    ILogger<ShelfService> logger,
    IUserContextService userContext,
    IBackgroundJobClient backgroundJobs,
    IRepository<Shelf> shelfRepository,
    IRepository<ShelfFolder> folderRepository,
    IRepository<ShelfUserAccess> shelfUserAccessRepository,
    IRepository<ShelfRoleAccess> shelfRoleAccessRepository,
    IRepository<Book> bookRepository,
    IStoragePathProvider storage) : IShelfService
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

        var shelfIds = shelfList.Select(s => s.Id).ToList();
        var bannerSources = await LoadShelfBannerSourcesAsync(shelfIds, cancellationToken);

        IReadOnlyList<ShelfDto> result = shelfList
            .Select(s =>
            {
                var preview = CardBannerSupport.BuildPreview(
                    s.CardBannerMode,
                    s.CardBannerImageFileName,
                    s.CardBannerBookIdsJson,
                    Constants.CardBannerKinds.Shelves,
                    s.Id,
                    bannerSources.GetValueOrDefault(s.Id) ?? [],
                    storage);
                return MapShelf(s, counts.GetValueOrDefault(s.Id, 0), preview, BannerSettings: null);
            })
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<ShelfDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
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

        var bannerSources = await LoadShelfBannerSourcesAsync([id], cancellationToken);
        var preview = CardBannerSupport.BuildPreview(
            shelf.CardBannerMode,
            shelf.CardBannerImageFileName,
            shelf.CardBannerBookIdsJson,
            Constants.CardBannerKinds.Shelves,
            shelf.Id,
            bannerSources.GetValueOrDefault(id) ?? [],
            storage);

        var settings = CardBannerSupport.BuildSettings(
            shelf.CardBannerMode,
            shelf.CardBannerImageFileName,
            shelf.CardBannerBookIdsJson,
            Constants.CardBannerKinds.Shelves,
            shelf.Id,
            storage);

        return Result.Success(MapShelf(shelf, bookCount, preview, settings));
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

        string? folderConflictShelf = await FindConflictingShelfNameForFolderPathsAsync(folderPaths, excludeShelfId: null, cancellationToken);
        if (folderConflictShelf is not null)
        {
            return Result.Conflict($"The selected folder is already mapped to the shelf, '{folderConflictShelf}'.");
        }

        var shelf = await shelfRepository.InsertAsync(new Shelf
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            DirectoryStructure = request.DirectoryStructure,
            AlwaysUseFileNameForTitle = request.AlwaysUseFileNameForTitle,
            AlwaysIgnoreAuthor = request.AlwaysIgnoreAuthor,
            AlwaysIgnoreTags = request.AlwaysIgnoreTags,
            AlwaysIgnoreGenres = request.AlwaysIgnoreGenres,
            AssignNewBooksToCollection = request.AssignNewBooksToCollection,
            NewBooksCollectionName = NormalizeCollectionName(request.AssignNewBooksToCollection, request.NewBooksCollectionName),
            AutoFetchOnlineMetadata = request.AutoFetchOnlineMetadata,
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
        // DirectoryStructure is deliberately not updated — it is fixed at creation time.
        shelf.AlwaysUseFileNameForTitle = request.AlwaysUseFileNameForTitle;
        shelf.AlwaysIgnoreAuthor = request.AlwaysIgnoreAuthor;
        shelf.AlwaysIgnoreTags = request.AlwaysIgnoreTags;
        shelf.AlwaysIgnoreGenres = request.AlwaysIgnoreGenres;
        shelf.AssignNewBooksToCollection = request.AssignNewBooksToCollection;
        shelf.NewBooksCollectionName = NormalizeCollectionName(request.AssignNewBooksToCollection, request.NewBooksCollectionName);
        shelf.AutoFetchOnlineMetadata = request.AutoFetchOnlineMetadata;
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
        var newPaths = desiredFolders.Where(p => !existingPaths.Contains(p)).ToList();
        string? folderConflictShelf = await FindConflictingShelfNameForFolderPathsAsync(newPaths, excludeShelfId: id, cancellationToken);
        if (folderConflictShelf is not null)
        {
            return Result.Conflict($"The selected folder is already mapped to the shelf, '{folderConflictShelf}'.");
        }

        var toAdd = newPaths
            .Select(p => new ShelfFolder { ShelfId = id, Path = p })
            .ToList();
        if (toAdd.Count > 0)
        {
            await folderRepository.InsertAsync(toAdd);
        }

        await SyncShelfAccessAsync(id, request.AllowedUserIds, request.AllowedRoleNames, cancellationToken);

        var memberIds = (await bookRepository.FindAsync(
            new SearchOptions<Book> { Query = b => b.ShelfId == id },
            b => b.Id)).ToHashSet();

        var bannerResult = CardBannerSupport.ApplyBannerUpdate(
            Constants.CardBannerKinds.Shelves,
            id,
            shelf,
            request.CardBannerMode,
            request.CardBannerSelectedBookIds,
            memberIds,
            storage,
            modeFieldName: nameof(request.CardBannerMode),
            selectedBooksFieldName: nameof(request.CardBannerSelectedBookIds),
            entityNoun: "shelf");
        if (!bannerResult.IsSuccess)
        {
            return Result<ShelfDto>.Invalid(bannerResult.ValidationErrors);
        }

        await shelfRepository.UpdateAsync(shelf);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<Result> UploadCardBannerAsync(
        int shelfId,
        Stream content,
        string fileName,
        long? contentLength,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = x => x.Id == shelfId,
            CancellationToken = cancellationToken,
        });
        if (shelf is null)
        {
            return Result.NotFound($"Shelf {shelfId} not found.");
        }

        const long maxBytes = 2_000_000;
        if (contentLength is > maxBytes)
        {
            return Result.Invalid(new ValidationError(nameof(fileName), $"Image must be at most {maxBytes / 1_000_000} MB."));
        }

        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".webp"))
        {
            return Result.Invalid(new ValidationError(nameof(fileName), "Use JPG, PNG, GIF, or WebP."));
        }

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        if (ms.Length > maxBytes)
        {
            return Result.Invalid(new ValidationError(nameof(fileName), $"Image must be at most {maxBytes / 1_000_000} MB."));
        }

        ms.Position = 0;
        string relative = await storage.SaveCardBannerFileAsync(
            Constants.CardBannerKinds.Shelves,
            shelfId,
            ms,
            fileName,
            cancellationToken);

        shelf.CardBannerMode = CardHeaderBannerMode.UploadedImage;
        shelf.CardBannerImageFileName = relative;
        shelf.CardBannerBookIdsJson = null;
        await shelfRepository.UpdateAsync(shelf);

        return Result.Success();
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

        storage.DeleteCardBannerFile(Constants.CardBannerKinds.Shelves, id);

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
        var users = (userIds ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var roles = (roleNames ?? [])
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

    /// <summary>
    /// When non-null, at least one path in <paramref name="normalizedPaths"/> is already
    /// mapped on another shelf (same path after trim; case-insensitive match).
    /// </summary>
    private async Task<string?> FindConflictingShelfNameForFolderPathsAsync(
        IReadOnlyList<string> normalizedPaths,
        int? excludeShelfId,
        CancellationToken cancellationToken)
    {
        if (normalizedPaths.Count == 0)
        {
            return null;
        }

        var wanted = normalizedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = await folderRepository.FindAsync(new SearchOptions<ShelfFolder>
        {
            Query = excludeShelfId is int sid ? f => f.ShelfId != sid : f => true,
            Include = q => q.Include(f => f.Shelf),
            CancellationToken = cancellationToken,
        });

        foreach (var row in rows)
        {
            string rowNorm = NormalizeFolderPath(row.Path);
            if (!wanted.Contains(rowNorm))
            {
                continue;
            }

            return row.Shelf?.Name ?? $"Shelf #{row.ShelfId}";
        }

        return null;
    }

    private static string NormalizeFolderPath(string path)
        => path.Trim().TrimEnd('/', '\\');

    private static List<string> NormalizeFolders(IReadOnlyList<string> folders)
        => [.. folders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizeFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static ShelfDto MapShelf(Shelf shelf, int bookCount, CardBannerPreview preview, CardBannerSettingsDto? BannerSettings = null)
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
            allowedRoles,
            shelf.DirectoryStructure,
            shelf.AlwaysUseFileNameForTitle,
            shelf.AlwaysIgnoreAuthor,
            shelf.AlwaysIgnoreTags,
            shelf.AlwaysIgnoreGenres,
            shelf.AssignNewBooksToCollection,
            shelf.NewBooksCollectionName,
            shelf.AutoFetchOnlineMetadata,
            preview,
            BannerSettings);
    }

    /// <summary>
    /// Returns a trimmed collection name when the shelf files new books into a collection, falling
    /// back to <see cref="Constants.DefaultNewBooksCollectionName"/> when none was supplied. Returns
    /// null when the feature is off so we don't persist a stale name.
    /// </summary>
    private static string? NormalizeCollectionName(bool assignNewBooksToCollection, string? name)
    {
        if (!assignNewBooksToCollection)
        {
            return null;
        }

        string trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? Constants.DefaultNewBooksCollectionName : trimmed;
    }

    private async Task<Dictionary<int, List<CardBannerSupport.BookCoverSource>>> LoadShelfBannerSourcesAsync(
        IReadOnlyList<int> shelfIds,
        CancellationToken cancellationToken)
    {
        if (shelfIds.Count == 0)
        {
            return [];
        }

        // Project to (BookId, ShelfId, CoverImagePath) to keep the wire payload small — we
        // only care about the cover existence + the owning shelf for grouping.
        var rows = await bookRepository.FindAsync(
            new SearchOptions<Book>
            {
                Query = b => shelfIds.Contains(b.ShelfId) && !string.IsNullOrEmpty(b.CoverImagePath),
                CancellationToken = cancellationToken,
            },
            b => new { b.Id, b.ShelfId, b.CoverImagePath });

        return CardBannerSupport.GroupCandidates(rows, r => r.ShelfId, r => r.Id, r => r.CoverImagePath);
    }
}