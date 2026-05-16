using LinqKit;
using Microsoft.EntityFrameworkCore;
using Shelfwarden.Models;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services;

public class AdditionalContentService(
    ILogger<AdditionalContentService> logger,
    IStoragePathProvider storage,
    IRepository<AdditionalContentItem> contentRepository,
    IRepository<AdditionalContentTag> additionalContentTagRepository,
    IRepository<AdditionalContentItemTag> additionalContentItemTagRepository,
    IRepository<BookAdditionalContentItem> bookContentRepository,
    IRepository<SeriesAdditionalContentItem> seriesContentRepository,
    IRepository<Author> authorRepository,
    IRepository<Series> seriesRepository) : IAdditionalContentService
{
    public async Task<Result<int>> ScanExtrasAsync(CancellationToken cancellationToken = default)
    {
        string extrasDir = storage.ExtrasDirectory;
        if (!Directory.Exists(extrasDir))
        {
            logger.LogInformation("Extras directory '{Dir}' does not exist; nothing to scan", extrasDir);
            return Result.Success(0);
        }

        var files = Directory.EnumerateFiles(extrasDir, "*.*", SearchOption.AllDirectories)
            .Where(ExtrasScanFileFilter.ShouldInclude);

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var existingByPath = (await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>()))
            .ToDictionary(i => i.FilePath, StringComparer.OrdinalIgnoreCase);

        var newItems = new List<AdditionalContentItem>();
        foreach (string filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string canonical = Path.GetFullPath(filePath);
            seenPaths.Add(canonical);

            if (existingByPath.ContainsKey(canonical))
            {
                continue;
            }

            var info = new FileInfo(canonical);
            newItems.Add(new AdditionalContentItem
            {
                FileName = info.Name,
                FilePath = canonical,
                FileExtension = info.Extension.ToLowerInvariant(),
                FileSizeBytes = info.Length,
                CreatedAt = DateTime.UtcNow,
            });
        }

        int added = 0;
        if (newItems.Count > 0)
        {
            await contentRepository.InsertAsync(newItems, ContextOptions.ForCancellationToken(cancellationToken));
            added = newItems.Count;
        }

        // Remove entries for files no longer on disk (extras scan only — never drop manually imported rows).
        var orphans = existingByPath
            .Where(kvp => !seenPaths.Contains(kvp.Key) && !kvp.Value.IsManuallyImported)
            .Select(kvp => kvp.Value)
            .ToList();

        int removedOrphans = 0;
        if (orphans.Count > 0)
        {
            await contentRepository.DeleteAsync(
                orphans,
                ContextOptions.ForCancellationToken(cancellationToken));

            removedOrphans = orphans.Count;
            logger.LogInformation("Removed {Count} orphaned extra content entries", removedOrphans);
        }

        logger.LogInformation("Extras scan complete — {Added} new, {Removed} removed", added, removedOrphans);
        return Result.Success(added);
    }

    /// <inheritdoc />
    public async Task<Result<RegisterExternalFilesResult>> RegisterExternalFilesAsync(
        IReadOnlyList<string> absoluteFilePaths,
        CancellationToken cancellationToken = default)
    {
        if (absoluteFilePaths.Count == 0)
        {
            return Result.Invalid(new ValidationError("paths", "Select at least one file."));
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in absoluteFilePaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            try
            {
                normalized.Add(Path.GetFullPath(raw.Trim()));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Skipping invalid path '{Path}'", raw);
            }
        }

        if (normalized.Count == 0)
        {
            return Result.Invalid(new ValidationError("paths", "No valid file paths were provided."));
        }

        var pathList = normalized.ToList();
        var existingItems = await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => pathList.Contains(i.FilePath),
            CancellationToken = cancellationToken,
        });

        var existingPaths = existingItems.Select(i => i.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toInsert = new List<AdditionalContentItem>();
        foreach (string fullPath in normalized)
        {
            if (existingPaths.Contains(fullPath))
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            if ((File.GetAttributes(fullPath) & FileAttributes.Directory) != 0)
            {
                continue;
            }

            if (!ExtrasScanFileFilter.ShouldInclude(fullPath))
            {
                continue;
            }

            var info = new FileInfo(fullPath);
            toInsert.Add(new AdditionalContentItem
            {
                FileName = info.Name,
                FilePath = fullPath,
                FileExtension = info.Extension.ToLowerInvariant(),
                FileSizeBytes = info.Length,
                CreatedAt = DateTime.UtcNow,
                IsManuallyImported = true,
            });
        }

        int skipped = normalized.Count - toInsert.Count;
        if (toInsert.Count == 0)
        {
            return Result.Success(new RegisterExternalFilesResult(0, skipped));
        }

        await contentRepository.InsertAsync(
            toInsert,
            ContextOptions.ForCancellationToken(cancellationToken));

        return Result.Success(new RegisterExternalFilesResult(toInsert.Count, skipped));
    }

    public async Task<Result<PagedList<AdditionalContentItemDto>>> ListPagedAsync(
        int page,
        int pageSize,
        int authorFilter = 0,
        int seriesFilter = 0,
        int? tagId = null,
        IReadOnlyList<int>? tagIds = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var predicate = PredicateBuilder.New<AdditionalContentItem>(true);
        if (authorFilter == -1)
        {
            predicate = predicate.And(i => i.AuthorId == null);
        }
        else if (authorFilter > 0)
        {
            predicate = predicate.And(i => i.AuthorId == authorFilter);
        }

        if (seriesFilter == -1)
        {
            predicate = predicate.And(i => !i.SeriesAdditionalContents.Any());
        }
        else if (seriesFilter > 0)
        {
            predicate = predicate.And(i => i.SeriesAdditionalContents.Any(s => s.SeriesId == seriesFilter));
        }

        var requestedTagIds = (tagIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (requestedTagIds.Count > 0)
        {
            predicate = predicate.And(i =>
                i.AdditionalContentItemTags
                    .Where(it => requestedTagIds.Contains(it.TagId))
                    .Select(it => it.TagId)
                    .Distinct()
                    .Count() == requestedTagIds.Count);
        }
        else if (tagId is int tagFilter)
        {
            if (tagFilter == -1)
            {
                predicate = predicate.And(i => !i.AdditionalContentItemTags.Any());
            }
            else if (tagFilter > 0)
            {
                predicate = predicate.And(i => i.AdditionalContentItemTags.Any(it => it.TagId == tagFilter));
            }
        }

        var options = new SearchOptions<AdditionalContentItem>
        {
            Query = predicate,
            PageNumber = page,
            PageSize = pageSize,
            Include = q => IncludeItemDetails(q),
            OrderBy = q => q.OrderBy(i => i.FileName),
            SplitQuery = true,
            CancellationToken = cancellationToken,
        };

        var records = await contentRepository.FindAsync(options);
        var results = records.Select(ToDto).ToList();
        return Result.Success(new PagedList<AdditionalContentItemDto>(results, records.ItemCount, page, pageSize));
    }

    public async Task<Result<IReadOnlyList<AdditionalContentTagDto>>> ListTagsAsync(
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<AdditionalContentTag>
        {
            OrderBy = q => q.OrderBy(t => t.NormalizedName),
            CancellationToken = cancellationToken,
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = t => EF.Functions.Like(t.NormalizedName, $"%{needle}%");
        }

        var rows = (await additionalContentTagRepository.FindAsync(options)).ToList();
        return Result.Success<IReadOnlyList<AdditionalContentTagDto>>(
            rows.Select(t => new AdditionalContentTagDto(t.Id, t.Name)).ToList());
    }

    public async Task<Result<IReadOnlyList<AdditionalContentTagDto>>> ListTagsForAuthorAsync(
        int authorId,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var predicate = PredicateBuilder.New<AdditionalContentTag>(
            t => t.AdditionalContentItemTags.Any(it => it.Item.AuthorId == authorId));

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            predicate = predicate.And(t => EF.Functions.Like(t.NormalizedName, $"%{needle}%"));
        }

        var rows = (await additionalContentTagRepository.FindAsync(new SearchOptions<AdditionalContentTag>
        {
            Query = predicate,
            OrderBy = q => q.OrderBy(t => t.NormalizedName),
            CancellationToken = cancellationToken,
        })).ToList();

        return Result.Success<IReadOnlyList<AdditionalContentTagDto>>(
            rows.Select(t => new AdditionalContentTagDto(t.Id, t.Name)).ToList());
    }

    public async Task<Result> SetItemsTagsAsync(
        SetAdditionalContentTagsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ItemIds.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(request.ItemIds), "Select at least one item."));
        }

        var distinctIds = request.ItemIds.Distinct().ToList();
        foreach (int itemId in distinctIds)
        {
            var item = await contentRepository.FindOneAsync(new SearchOptions<AdditionalContentItem>
            {
                Query = i => i.Id == itemId,
                CancellationToken = cancellationToken,
            });

            if (item is null)
            {
                logger.LogWarning("Extra content item {Id} not found; skipping tag update", itemId);
                continue;
            }

            await SyncItemTagsAsync(itemId, request.TagNames, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<AdditionalContentItemDto>>> GetForAuthorAsync(
        int authorId,
        CancellationToken cancellationToken = default)
    {
        var items = await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.AuthorId == authorId,
            Include = q => IncludeItemDetails(q),
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
            Include = q => IncludeItemDetails(q),
            CancellationToken = cancellationToken,
        })).ToList();

        // Items linked to any book belonging to this series.
        var viaBooks = (await contentRepository.FindAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.BookAdditionalContents.Any(b => b.Book.SeriesId == seriesId),
            Include = q => IncludeItemDetails(q),
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
            Include = q => IncludeItemDetails(q),
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

        var ctx = ContextOptions.ForCancellationToken(cancellationToken);

        foreach (int itemId in request.ItemIds.Distinct())
        {
            var item = await contentRepository.FindOneAsync(new SearchOptions<AdditionalContentItem>
            {
                Query = i => i.Id == itemId,
                CancellationToken = cancellationToken,
            });

            if (item is null)
            {
                logger.LogWarning("Extra content item {Id} not found; skipping assignment", itemId);
                continue;
            }

            if (item.AuthorId == request.AuthorId)
            {
                logger.LogDebug(
                    "Extra content item {Id} is already assigned to author {AuthorId}; skipping",
                    itemId,
                    request.AuthorId);
                continue;
            }

            // Moving from one author to another: drop book/series links and relocate under the new author's root only.
            bool reassigningToDifferentAuthor =
                item.AuthorId.HasValue && item.AuthorId.Value != request.AuthorId;

            if (reassigningToDifferentAuthor)
            {
                await bookContentRepository.DeleteAsync(
                    bc => bc.AdditionalContentItemId == itemId,
                    ctx);
                await seriesContentRepository.DeleteAsync(
                    sc => sc.AdditionalContentItemId == itemId,
                    ctx);
            }

            string oldFullPath = Path.GetFullPath(item.FilePath);
            string? oldContainingDir = Path.GetDirectoryName(oldFullPath);

            if (!item.IsManuallyImported)
            {
                // Admin "assign to author" stores scanned extras under the author's root folder.
                string targetDir = authorDir;
                string newPath = MoveFile(item.FilePath, targetDir);
                string newFullPath = Path.GetFullPath(newPath);

                if (!string.Equals(oldFullPath, newFullPath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(newFullPath))
                {
                    TryPruneEmptyExtraDirectories(oldContainingDir, storage.ExtrasDirectory);
                }

                item.FilePath = newPath;
            }

            item.AuthorId = author.Id;

            // One update per item so EF never tries to attach multiple graphs that share the same tracked Series.
            await contentRepository.UpdateAsync(item, ctx);
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

        var newLinks = request.Ids
            .Where(id => !existing.Contains(id))
            .Select(bookId => new BookAdditionalContentItem
            {
                BookId = bookId,
                AdditionalContentItemId = request.ItemId,
            })
            .ToList();

        if (newLinks.Count > 0)
        {
            await bookContentRepository.InsertAsync(
                newLinks,
                ContextOptions.ForCancellationToken(cancellationToken));
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

        var newSeriesLinks = request.Ids
            .Where(id => !existing.Contains(id))
            .Select(seriesId => new SeriesAdditionalContentItem
            {
                SeriesId = seriesId,
                AdditionalContentItemId = request.ItemId,
            })
            .ToList();

        if (newSeriesLinks.Count > 0)
        {
            await seriesContentRepository.InsertAsync(
                newSeriesLinks,
                ContextOptions.ForCancellationToken(cancellationToken));
        }

        // Move file into the series subdirectory when the author is known and exactly one series
        // (skipped for manually imported files — they stay at their original path).
        if (!item.IsManuallyImported && item.Author is not null && request.Ids.Count == 1)
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
            if (item.IsManuallyImported)
            {
                // Library entry only — do not delete the user's original file.
                continue;
            }

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
        }

        if (items.Count > 0)
        {
            await contentRepository.DeleteAsync(
                items,
                ContextOptions.ForCancellationToken(cancellationToken));
        }

        return Result.Success();
    }

    public async Task<Result<AdditionalContentItemDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await contentRepository.FindOneAsync(new SearchOptions<AdditionalContentItem>
        {
            Query = i => i.Id == id,
            Include = q => IncludeItemDetails(q),
            CancellationToken = cancellationToken,
        });

        if (item is null)
        {
            return Result.NotFound($"Content item {id} not found.");
        }

        return Result.Success(ToDto(item));
    }

    /// <inheritdoc />
    public async Task<Result<string>> GetViewableTextAsync(int id, CancellationToken cancellationToken = default)
    {
        var itemResult = await GetByIdAsync(id, cancellationToken);
        if (!itemResult.IsSuccess)
        {
            string msg = itemResult.Errors.FirstOrDefault() ?? "Could not load content item.";
            return itemResult.Status switch
            {
                ResultStatus.NotFound => Result.NotFound(msg),
                _ => Result.Error(msg),
            };
        }

        var dto = itemResult.Value;
        string ext = dto.FileExtension.ToLowerInvariant();
        if (ext is not (".txt" or ".md" or ".html" or ".htm"))
        {
            return Result.Invalid(new ValidationError(nameof(id), "This file type is not opened as text in the viewer."));
        }

        if (!File.Exists(dto.FilePath))
        {
            return Result.NotFound($"Content file for id {id} is missing on disk.");
        }

        try
        {
            string text = await File.ReadAllTextAsync(dto.FilePath, System.Text.Encoding.UTF8, cancellationToken);
            return Result.Success(text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read extra content file '{Path}' for id {Id}", dto.FilePath, id);
            return Result.Error("Unable to read file.");
        }
    }

    // ----- helpers -----

    private static IQueryable<AdditionalContentItem> IncludeItemDetails(IQueryable<AdditionalContentItem> q) =>
        q.Include(i => i.Author)
            .Include(i => i.BookAdditionalContents).ThenInclude(b => b.Book)
            .Include(i => i.SeriesAdditionalContents).ThenInclude(s => s.Series)
            .Include(i => i.AdditionalContentItemTags).ThenInclude(it => it.Tag);

    private async Task SyncItemTagsAsync(
        int itemId,
        IReadOnlyList<string> tagNames,
        CancellationToken cancellationToken)
    {
        var normalised = tagNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lookup = normalised.Select(n => n.ToLowerInvariant()).ToList();
        var existingTags = (await additionalContentTagRepository.FindAsync(new SearchOptions<AdditionalContentTag>
        {
            Query = t => lookup.Contains(t.NormalizedName),
            CancellationToken = cancellationToken,
        })).ToList();

        var byNormalized = existingTags.ToDictionary(t => t.NormalizedName, StringComparer.OrdinalIgnoreCase);

        var toCreate = new List<AdditionalContentTag>();
        foreach (string name in normalised)
        {
            string key = name.ToLowerInvariant();
            if (!byNormalized.ContainsKey(key))
            {
                toCreate.Add(new AdditionalContentTag { Name = name, NormalizedName = key });
            }
        }

        if (toCreate.Count > 0)
        {
            var created = await additionalContentTagRepository.InsertAsync(
                toCreate,
                ContextOptions.ForCancellationToken(cancellationToken));
            foreach (var t in created)
            {
                byNormalized[t.NormalizedName] = t;
            }
        }

        var existingJoins = (await additionalContentItemTagRepository.FindAsync(new SearchOptions<AdditionalContentItemTag>
        {
            Query = it => it.ItemId == itemId,
            CancellationToken = cancellationToken,
        })).ToList();

        var desiredTagIds = normalised
            .Select(n => byNormalized[n.ToLowerInvariant()].Id)
            .ToHashSet();

        var toRemove = existingJoins.Where(it => !desiredTagIds.Contains(it.TagId)).ToList();
        if (toRemove.Count > 0)
        {
            await additionalContentItemTagRepository.DeleteAsync(
                toRemove,
                ContextOptions.ForCancellationToken(cancellationToken));
        }

        var existingJoinIds = existingJoins.Select(it => it.TagId).ToHashSet();
        var toAdd = desiredTagIds
            .Where(tid => !existingJoinIds.Contains(tid))
            .Select(tid => new AdditionalContentItemTag { ItemId = itemId, TagId = tid })
            .ToList();

        if (toAdd.Count > 0)
        {
            await additionalContentItemTagRepository.InsertAsync(
                toAdd,
                ContextOptions.ForCancellationToken(cancellationToken));
        }
    }

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
            item.IsManuallyImported,
            item.AdditionalContentItemTags
                .OrderBy(it => it.Tag.NormalizedName)
                .Select(it => new AdditionalContentTagDto(it.Tag.Id, it.Tag.Name))
                .ToList(),
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

        // Resolve name collisions: "name.jpg" -> "name - 1.jpg" -> "name - 2.jpg" ...
        if (File.Exists(destPath))
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int counter = 1;
            do
            {
                destPath = Path.Combine(targetDir, $"{nameWithoutExt} - {counter}{ext}");
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

    /// <summary>
    /// Deletes empty directories upward from <paramref name="leafDirectory"/> until
    /// <paramref name="extrasRoot"/> is reached, so orphaned series/author folders disappear after moves.
    /// </summary>
    private void TryPruneEmptyExtraDirectories(string? leafDirectory, string extrasRoot)
    {
        if (string.IsNullOrWhiteSpace(leafDirectory))
        {
            return;
        }

        try
        {
            string normExtras = Path.GetFullPath(extrasRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? dir = Path.GetFullPath(leafDirectory);

            while (dir is not null)
            {
                // Never delete the extras root; only prune strict subdirectories (avoids "Extras..." prefix false positives).
                if (string.Equals(dir, normExtras, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (!dir.StartsWith(normExtras + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !dir.StartsWith(normExtras + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (!Directory.Exists(dir))
                {
                    break;
                }

                if (Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    break;
                }

                Directory.Delete(dir, recursive: false);
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not prune empty extras directories under '{Dir}'", leafDirectory);
        }
    }
}
