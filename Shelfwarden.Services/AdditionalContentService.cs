using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services;

public class AdditionalContentService(
    ILogger<AdditionalContentService> logger,
    IStoragePathProvider storage,
    IRepository<AdditionalContentItem> contentRepository,
    IRepository<BookAdditionalContentItem> bookContentRepository,
    IRepository<SeriesAdditionalContentItem> seriesContentRepository,
    IRepository<Author> authorRepository,
    IRepository<Series> seriesRepository,
    IRepository<Book> bookRepository) : IAdditionalContentService
{
    private static readonly HashSet<string> EbookExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".epub", ".pdf", ".mobi", ".azw", ".azw3", ".cbz", ".cbr" };

    public async Task<Result<int>> ScanExtrasAsync(CancellationToken cancellationToken = default)
    {
        string extrasDir = storage.ExtrasDirectory;
        if (!Directory.Exists(extrasDir))
        {
            logger.LogInformation("Extras directory '{Dir}' does not exist; nothing to scan", extrasDir);
            return Result.Success(0);
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(extrasDir, "*.*", SearchOption.AllDirectories)
                .Where(f => !EbookExtensions.Contains(Path.GetExtension(f)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enumerate extras directory '{Dir}'", extrasDir);
            return Result.Error("Failed to enumerate extras directory.");
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingByPath = (await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>()))
            .ToDictionary(i => i.FilePath, StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (string filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string canonical = Path.GetFullPath(filePath);
            seenPaths.Add(canonical);

            if (existingByPath.ContainsKey(canonical))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(canonical);
                var item = new AdditionalContentItem
                {
                    FileName = info.Name,
                    FilePath = canonical,
                    FileExtension = info.Extension.ToLowerInvariant(),
                    FileSizeBytes = info.Length,
                    CreatedAt = DateTime.UtcNow,
                };
                await contentRepository.InsertAsync(item);
                added++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to add extra content item for '{Path}'", filePath);
            }
        }

        // Remove entries for files no longer on disk.
        var orphans = existingByPath
            .Where(kvp => !seenPaths.Contains(kvp.Key))
            .Select(kvp => kvp.Value)
            .ToList();

        foreach (var orphan in orphans)
        {
            try
            {
                await contentRepository.DeleteAsync(orphan);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to remove orphaned extra content item {Id}", orphan.Id);
            }
        }

        if (orphans.Count > 0)
        {
            logger.LogInformation("Removed {Count} orphaned extra content entries", orphans.Count);
        }

        logger.LogInformation("Extras scan complete — {Added} new, {Removed} removed", added, orphans.Count);
        return Result.Success(added);
    }

    public async Task<Result<IReadOnlyList<AdditionalContentItemDto>>> ListAsync(
        int? authorId = null,
        int? seriesId = null,
        CancellationToken cancellationToken = default)
    {
        var items = (await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Include = q => q
                .Include(i => i.Author)
                .Include(i => i.BookAdditionalContents).ThenInclude(b => b.Book)
                .Include(i => i.SeriesAdditionalContents).ThenInclude(s => s.Series),
            OrderBy = q => q.OrderBy(i => i.FileName),
            CancellationToken = cancellationToken,
        })).ToList();

        // Filter by authorId: 0 = unassigned, positive = specific author.
        if (authorId.HasValue)
        {
            items = authorId.Value == 0
                ? items.Where(i => i.AuthorId is null).ToList()
                : items.Where(i => i.AuthorId == authorId.Value).ToList();
        }

        // Filter by seriesId: 0 = not associated with any series, positive = specific series.
        if (seriesId.HasValue)
        {
            items = seriesId.Value == 0
                ? items.Where(i => i.SeriesAdditionalContents.Count == 0).ToList()
                : items.Where(i => i.SeriesAdditionalContents.Any(s => s.SeriesId == seriesId.Value)).ToList();
        }

        return Result.Success<IReadOnlyList<AdditionalContentItemDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetForAuthorAsync(
        int authorId,
        CancellationToken cancellationToken = default)
    {
        var items = await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.AuthorId == authorId,
            Include = q => q
                .Include(i => i.Author)
                .Include(i => i.BookAdditionalContents).ThenInclude(b => b.Book)
                .Include(i => i.SeriesAdditionalContents).ThenInclude(s => s.Series),
            OrderBy = q => q.OrderBy(i => i.FileName),
            CancellationToken = cancellationToken,
        });

        return Result.Success<IReadOnlyList<AdditionalContentItemDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetForSeriesAsync(
        int seriesId,
        CancellationToken cancellationToken = default)
    {
        // Items directly linked to the series.
        var directlyLinked = (await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.SeriesAdditionalContents.Any(s => s.SeriesId == seriesId),
            Include = q => q
                .Include(i => i.Author)
                .Include(i => i.BookAdditionalContents).ThenInclude(b => b.Book)
                .Include(i => i.SeriesAdditionalContents).ThenInclude(s => s.Series),
            CancellationToken = cancellationToken,
        })).ToList();

        // Items linked to any book belonging to this series.
        var viaBooks = (await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.BookAdditionalContents.Any(b => b.Book.SeriesId == seriesId),
            Include = q => q
                .Include(i => i.Author)
                .Include(i => i.BookAdditionalContents).ThenInclude(b => b.Book)
                .Include(i => i.SeriesAdditionalContents).ThenInclude(s => s.Series),
            CancellationToken = cancellationToken,
        })).ToList();

        var combined = directlyLinked
            .Concat(viaBooks)
            .DistinctBy(i => i.Id)
            .OrderBy(i => i.FileName)
            .Select(ToDto)
            .ToList();

        return Result.Success<IReadOnlyList<AdditionalContentItemDto>>(combined);
    }

    public async Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetForBookAsync(
        int bookId,
        CancellationToken cancellationToken = default)
    {
        var items = await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.BookAdditionalContents.Any(b => b.BookId == bookId),
            Include = q => q
                .Include(i => i.Author)
                .Include(i => i.BookAdditionalContents).ThenInclude(b => b.Book)
                .Include(i => i.SeriesAdditionalContents).ThenInclude(s => s.Series),
            OrderBy = q => q.OrderBy(i => i.FileName),
            CancellationToken = cancellationToken,
        });

        return Result.Success<IReadOnlyList<AdditionalContentItemDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result> AssignToAuthorAsync(
        AssignContentToAuthorRequest request,
        CancellationToken cancellationToken = default)
    {
        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == request.AuthorId,
            CancellationToken = cancellationToken,
        });

        if (author is null)
        {
            return Result.NotFound($"Author {request.AuthorId} not found.");
        }

        string authorFolderName = SanitizeFolderName(author.Name);
        string authorDir = Path.Combine(storage.ExtrasDirectory, authorFolderName);
        Directory.CreateDirectory(authorDir);

        foreach (int itemId in request.ItemIds)
        {
            var item = await contentRepository.FindOneAsync(new SearchOptions<AdditionalContentItem>
            {
                Query = i => i.Id == itemId,
                Include = q => q.Include(i => i.SeriesAdditionalContents).ThenInclude(s => s.Series),
                CancellationToken = cancellationToken,
            });

            if (item is null)
            {
                logger.LogWarning("Extra content item {Id} not found; skipping assignment", itemId);
                continue;
            }

            // Move the file to the author (or author/series) directory.
            string targetDir = authorDir;
            var firstSeries = item.SeriesAdditionalContents.Select(s => s.Series).FirstOrDefault();
            if (firstSeries is not null)
            {
                string seriesFolderName = SanitizeFolderName(firstSeries.Name);
                targetDir = Path.Combine(authorDir, seriesFolderName);
                Directory.CreateDirectory(targetDir);
            }

            string newPath = MoveFile(item.FilePath, targetDir);
            item.FilePath = newPath;
            item.AuthorId = author.Id;
            await contentRepository.UpdateAsync(item);
        }

        return Result.Success();
    }

    public async Task<Result> AssociateWithBooksAsync(
        AssociateContentRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = await contentRepository.FindOneAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.Id == request.ItemId,
            CancellationToken = cancellationToken,
        });

        if (item is null)
        {
            return Result.NotFound($"Content item {request.ItemId} not found.");
        }

        // Only add new associations; never remove existing ones so the same item
        // can be linked to multiple books across separate "Assign" actions.
        var existing = (await bookContentRepository.FindAsync(new SearchOptions<BookAdditionalContentItem>
        {
            Query = bc => bc.AdditionalContentItemId == request.ItemId,
            CancellationToken = cancellationToken,
        })).Select(bc => bc.BookId).ToHashSet();

        foreach (int bookId in request.Ids.Where(id => !existing.Contains(id)))
        {
            await bookContentRepository.InsertAsync(new BookAdditionalContentItem
            {
                BookId = bookId,
                AdditionalContentItemId = request.ItemId,
            });
        }

        return Result.Success();
    }

    public async Task<Result> AssociateWithSeriesAsync(
        AssociateContentRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = await contentRepository.FindOneAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.Id == request.ItemId,
            Include = q => q.Include(i => i.Author),
            CancellationToken = cancellationToken,
        });

        if (item is null)
        {
            return Result.NotFound($"Content item {request.ItemId} not found.");
        }

        // Only add new series associations; preserve any existing ones.
        var existing = (await seriesContentRepository.FindAsync(new SearchOptions<SeriesAdditionalContentItem>
        {
            Query = sc => sc.AdditionalContentItemId == request.ItemId,
            CancellationToken = cancellationToken,
        })).Select(sc => sc.SeriesId).ToHashSet();

        foreach (int seriesId in request.Ids.Where(id => !existing.Contains(id)))
        {
            await seriesContentRepository.InsertAsync(new SeriesAdditionalContentItem
            {
                SeriesId = seriesId,
                AdditionalContentItemId = request.ItemId,
            });
        }

        // Move file into the series subdirectory when the author is known and exactly one series.
        if (item.Author is not null && request.Ids.Count == 1)
        {
            var series = await seriesRepository.FindOneAsync(new SearchOptions<Series>
            {
                Query = s => s.Id == request.Ids[0],
                CancellationToken = cancellationToken,
            });

            if (series is not null)
            {
                string authorDir = Path.Combine(storage.ExtrasDirectory, SanitizeFolderName(item.Author.Name));
                string seriesDir = Path.Combine(authorDir, SanitizeFolderName(series.Name));
                Directory.CreateDirectory(seriesDir);

                string newPath = MoveFile(item.FilePath, seriesDir);
                if (newPath != item.FilePath)
                {
                    item.FilePath = newPath;
                    await contentRepository.UpdateAsync(item);
                }
            }
        }

        return Result.Success();
    }

    public async Task<Result> RenameAsync(RenameContentItemRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewFileName))
        {
            return Result.Invalid(new ValidationError("NewFileName", "File name cannot be empty."));
        }

        var item = await contentRepository.FindOneAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.Id == request.ItemId,
            CancellationToken = cancellationToken,
        });

        if (item is null)
        {
            return Result.NotFound($"Content item {request.ItemId} not found.");
        }

        item.FileName = request.NewFileName.Trim();
        await contentRepository.UpdateAsync(item);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default)
    {
        var items = (await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => itemIds.Contains(i.Id),
            CancellationToken = cancellationToken,
        })).ToList();

        foreach (var item in items)
        {
            try
            {
                if (File.Exists(item.FilePath))
                {
                    File.Delete(item.FilePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete extra content file '{Path}'", item.FilePath);
            }

            await contentRepository.DeleteAsync(item);
        }

        return Result.Success();
    }

    public async Task<Result<AdditionalContentItemDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await contentRepository.FindOneAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.Id == id,
            Include = q => q
                .Include(i => i.Author)
                .Include(i => i.BookAdditionalContents).ThenInclude(b => b.Book)
                .Include(i => i.SeriesAdditionalContents).ThenInclude(s => s.Series),
            CancellationToken = cancellationToken,
        });

        if (item is null)
        {
            return Result.NotFound($"Content item {id} not found.");
        }

        return Result.Success(ToDto(item));
    }

    // ----- helpers -----

    private static AdditionalContentItemDto ToDto(AdditionalContentItem item) =>
        new(
            item.Id,
            item.FileName,
            item.FilePath,
            item.FileExtension,
            item.FileSizeBytes,
            item.CreatedAt,
            item.AuthorId,
            item.Author?.Name,
            item.BookAdditionalContents.Select(b => new AdditionalContentAssociationDto(b.Book.Id, b.Book.Title)).ToList(),
            item.SeriesAdditionalContents.Select(s => new AdditionalContentAssociationDto(s.Series.Id, s.Series.Name)).ToList());

    private static string SanitizeFolderName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(name.Where(c => !invalid.Contains(c)).ToArray());
        return safe.Trim().Length == 0 ? "_unknown" : safe.Trim();
    }

    /// <summary>
    /// Moves <paramref name="sourcePath"/> into <paramref name="targetDir"/>, handling
    /// name collisions by appending a counter. Returns the new absolute path.
    /// </summary>
    private string MoveFile(string sourcePath, string targetDir)
    {
        if (!File.Exists(sourcePath))
        {
            logger.LogWarning("Source file '{Path}' does not exist; cannot move", sourcePath);
            return sourcePath;
        }

        string fileName = Path.GetFileName(sourcePath);
        string destPath = Path.Combine(targetDir, fileName);

        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
        {
            return sourcePath;
        }

        // Resolve name collisions.
        if (File.Exists(destPath))
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int counter = 1;
            do
            {
                destPath = Path.Combine(targetDir, $"{nameWithoutExt}_{counter}{ext}");
                counter++;
            }
            while (File.Exists(destPath));
        }

        try
        {
            File.Move(sourcePath, destPath);
            return destPath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to move file from '{Src}' to '{Dst}'", sourcePath, destPath);
            return sourcePath;
        }
    }
}
