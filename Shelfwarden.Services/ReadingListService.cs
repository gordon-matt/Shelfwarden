using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services;

public class ReadingListService(
    IUserContextService userContext,
    IRepository<ReadingList> listRepository,
    IRepository<ReadingListItem> itemRepository,
    IRepository<Book> bookRepository,
    IRepository<BookProgress> progressRepository,
    IStoragePathProvider storage) : IReadingListService
{
    public async Task<Result<IReadOnlyList<ReadingListDto>>> ListAsync(int? shelfId = null, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        HashSet<int>? onShelf = null;
        if (shelfId is int sid)
        {
            onShelf = [];
            const int pageSize = 5000;
            int page = 1;
            while (true)
            {
                var chunk = (await itemRepository.FindAsync(
                    new SearchOptions<ReadingListItem>
                    {
                        Query = i => i.Book.ShelfId == sid,
                        PageNumber = page,
                        PageSize = pageSize,
                        CancellationToken = cancellationToken,
                    },
                    i => i.ReadingListId)).ToList();

                if (chunk.Count == 0)
                {
                    break;
                }

                foreach (int listId in chunk)
                {
                    onShelf.Add(listId);
                }

                if (chunk.Count < pageSize)
                {
                    break;
                }

                page++;
            }
        }

        var rows = (await listRepository.FindAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.OwnerUserId == userId,
            OrderBy = q => q.OrderBy(l => l.Name),
            CancellationToken = cancellationToken,
        })).ToList();

        if (onShelf is not null)
        {
            rows = rows.Where(l => onShelf.Contains(l.Id)).ToList();
        }

        var ids = rows.Select(l => l.Id).ToList();
        var counts = (await itemRepository
            .FindAsync(
                new SearchOptions<ReadingListItem> { Query = i => ids.Contains(i.ReadingListId) },
                i => i.ReadingListId))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        var bannerSources = await LoadReadingListBannerSourcesAsync(ids, cancellationToken);

        IReadOnlyList<ReadingListDto> dtos = rows
            .Select(l =>
            {
                var preview = CardBannerSupport.BuildPreview(
                    l.CardBannerMode,
                    l.CardBannerImageFileName,
                    l.CardBannerBookIdsJson,
                    CardBannerSupport.KindReadingLists,
                    l.Id,
                    bannerSources.GetValueOrDefault(l.Id) ?? [],
                    storage);
                return Map(l, counts.GetValueOrDefault(l.Id, 0), preview);
            })
            .ToList();

        return Result.Success(dtos);
    }

    public async Task<Result<ReadingListDetailDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var list = await listRepository.FindOneAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.Id == id,
            CancellationToken = cancellationToken,
        });

        if (list is null)
        {
            return Result.NotFound();
        }

        if (list.OwnerUserId != userId)
        {
            return Result.Forbidden();
        }

        // Pull items in queue order. We then load the books separately so the BookListItemDto
        // projection has access to the navigations it needs (Series + BookAuthors.Author).
        var items = (await itemRepository.FindAsync(new SearchOptions<ReadingListItem>
        {
            Query = i => i.ReadingListId == id,
            OrderBy = q => q.OrderBy(i => i.Position),
            CancellationToken = cancellationToken,
        })).ToList();

        var bookIds = items.Select(i => i.BookId).ToList();
        var books = bookIds.Count == 0
            ? []
            : (await bookRepository.FindAsync(new SearchOptions<Book>
            {
                Query = b => bookIds.Contains(b.Id),
                Include = q => q.Include(b => b.Series).Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
                SplitQuery = true,
                CancellationToken = cancellationToken,
            })).ToList();

        var booksById = books.ToDictionary(b => b.Id);
        var progress = await BookProjections.LoadProgressPercentagesAsync(
            progressRepository, userId, bookIds, cancellationToken);

        // Skip any item whose book has been deleted under us — the cascade should keep these
        // in sync but we're defensive in case migrations get out of step.
        var entries = items
            .Where(i => booksById.ContainsKey(i.BookId))
            .Select(i => new ReadingListEntryDto(
                i.Id,
                i.Position,
                BookProjections.ToListItem(booksById[i.BookId], progress.GetValueOrDefault(i.BookId, 0))))
            .ToList();

        var settings = CardBannerSupport.BuildSettings(
            list.CardBannerMode,
            list.CardBannerImageFileName,
            list.CardBannerBookIdsJson,
            CardBannerSupport.KindReadingLists,
            list.Id,
            storage);

        return Result.Success(new ReadingListDetailDto(
            list.Id, list.Name, list.Description, list.OwnerUserId, list.CreatedAt, entries, settings));
    }

    public async Task<Result<ReadingListDto>> CreateAsync(CreateReadingListRequest request, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        string trimmedName = request.Name.Trim();
        var clash = await listRepository.FindOneAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.OwnerUserId == userId && l.Name == trimmedName,
            CancellationToken = cancellationToken,
        });
        if (clash is not null)
        {
            return Result.Conflict($"You already have a reading list called \"{trimmedName}\".");
        }

        var inserted = await listRepository.InsertAsync(new ReadingList
        {
            Name = trimmedName,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            OwnerUserId = userId,
            CreatedAt = DateTime.UtcNow,
        });

        var preview = CardBannerSupport.BuildPreview(
            CardHeaderBannerMode.RandomCovers,
            null,
            null,
            CardBannerSupport.KindReadingLists,
            inserted.Id,
            [],
            storage);

        return Result.Success(Map(inserted, BookCount: 0, preview));
    }

    public async Task<Result<ReadingListDto>> UpdateAsync(int id, UpdateReadingListRequest request, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var list = await listRepository.FindOneAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.Id == id,
            CancellationToken = cancellationToken,
        });
        if (list is null)
        {
            return Result.NotFound();
        }

        if (list.OwnerUserId != userId)
        {
            return Result.Forbidden();
        }

        list.Name = request.Name.Trim();
        list.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        var memberIds = (await itemRepository.FindAsync(
            new SearchOptions<ReadingListItem> { Query = i => i.ReadingListId == id },
            i => i.BookId)).ToHashSet();

        var bannerResult = ApplyReadingListBannerUpdateAsync(
            id, list, request.CardBannerMode, request.CardBannerSelectedBookIds, memberIds);
        if (!bannerResult.IsSuccess)
        {
            return Result<ReadingListDto>.Invalid(bannerResult.ValidationErrors);
        }

        var updated = await listRepository.UpdateAsync(list);

        int count = await itemRepository.CountAsync(i => i.ReadingListId == id);

        var bannerSources = await LoadReadingListBannerSourcesAsync([id], cancellationToken);
        var preview = CardBannerSupport.BuildPreview(
            updated.CardBannerMode,
            updated.CardBannerImageFileName,
            updated.CardBannerBookIdsJson,
            CardBannerSupport.KindReadingLists,
            updated.Id,
            bannerSources.GetValueOrDefault(id) ?? [],
            storage);

        return Result.Success(Map(updated, count, preview));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var list = await listRepository.FindOneAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.Id == id,
            CancellationToken = cancellationToken,
        });
        if (list is null)
        {
            return Result.NotFound();
        }

        if (list.OwnerUserId != userId)
        {
            return Result.Forbidden();
        }

        storage.DeleteCardBannerFile(CardBannerSupport.KindReadingLists, id);

        await listRepository.DeleteAsync(list);
        return Result.Success();
    }

    public async Task<Result> UploadCardBannerAsync(
        int readingListId,
        Stream content,
        string fileName,
        long? contentLength,
        CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var list = await listRepository.FindOneAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.Id == readingListId,
            CancellationToken = cancellationToken,
        });
        if (list is null)
        {
            return Result.NotFound();
        }

        if (list.OwnerUserId != userId)
        {
            return Result.Forbidden();
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
            CardBannerSupport.KindReadingLists,
            readingListId,
            ms,
            fileName,
            cancellationToken);

        list.CardBannerMode = CardHeaderBannerMode.UploadedImage;
        list.CardBannerImageFileName = relative;
        list.CardBannerBookIdsJson = null;
        await listRepository.UpdateAsync(list);

        return Result.Success();
    }

    public async Task<Result> AddBookAsync(int readingListId, int bookId, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var list = await listRepository.FindOneAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.Id == readingListId,
            CancellationToken = cancellationToken,
        });
        if (list is null)
        {
            return Result.NotFound("Reading list not found.");
        }

        if (list.OwnerUserId != userId)
        {
            return Result.Forbidden();
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });
        if (book is null)
        {
            return Result.NotFound("Book not found.");
        }

        var existing = await itemRepository.FindOneAsync(new SearchOptions<ReadingListItem>
        {
            Query = i => i.ReadingListId == readingListId && i.BookId == bookId,
            CancellationToken = cancellationToken,
        });
        if (existing is not null)
        {
            // Idempotent — same as Collections.
            return Result.Success();
        }

        int maxPosition = (await itemRepository.FindAsync(
                new SearchOptions<ReadingListItem> { Query = i => i.ReadingListId == readingListId },
                i => i.Position))
            .DefaultIfEmpty(-1)
            .Max();

        await itemRepository.InsertAsync(new ReadingListItem
        {
            ReadingListId = readingListId,
            BookId = bookId,
            Position = maxPosition + 1,
        });

        return Result.Success();
    }

    public async Task<Result> RemoveBookAsync(int readingListId, int bookId, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var list = await listRepository.FindOneAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.Id == readingListId,
            CancellationToken = cancellationToken,
        });
        if (list is null)
        {
            return Result.NotFound();
        }

        if (list.OwnerUserId != userId)
        {
            return Result.Forbidden();
        }

        var entry = await itemRepository.FindOneAsync(new SearchOptions<ReadingListItem>
        {
            Query = i => i.ReadingListId == readingListId && i.BookId == bookId,
            CancellationToken = cancellationToken,
        });
        if (entry is null)
        {
            return Result.NotFound();
        }

        await itemRepository.DeleteAsync(entry);

        // Compact positions so we don't develop holes (purely cosmetic — the OrderBy still
        // sorts correctly with gaps, but it makes Position values predictable for tests).
        await CompactPositionsAsync(readingListId, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReorderAsync(int readingListId, IReadOnlyList<int> orderedItemIds, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var list = await listRepository.FindOneAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.Id == readingListId,
            CancellationToken = cancellationToken,
        });
        if (list is null)
        {
            return Result.NotFound();
        }

        if (list.OwnerUserId != userId)
        {
            return Result.Forbidden();
        }

        var items = (await itemRepository.FindAsync(new SearchOptions<ReadingListItem>
        {
            Query = i => i.ReadingListId == readingListId,
            CancellationToken = cancellationToken,
        })).ToList();

        var byId = items.ToDictionary(i => i.Id);
        var toUpdate = new List<ReadingListItem>();
        int newPos = 0;

        foreach (int itemId in orderedItemIds)
        {
            if (!byId.TryGetValue(itemId, out var item))
            {
                continue;
            }

            if (item.Position != newPos)
            {
                item.Position = newPos;
                toUpdate.Add(item);
            }
            newPos++;
        }

        if (toUpdate.Count > 0)
        {
            await itemRepository.UpdateAsync(toUpdate);
        }

        return Result.Success();
    }

    private async Task CompactPositionsAsync(int readingListId, CancellationToken cancellationToken)
    {
        var items = (await itemRepository.FindAsync(new SearchOptions<ReadingListItem>
        {
            Query = i => i.ReadingListId == readingListId,
            OrderBy = q => q.OrderBy(i => i.Position),
            CancellationToken = cancellationToken,
        })).ToList();

        var toUpdate = new List<ReadingListItem>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Position != i)
            {
                items[i].Position = i;
                toUpdate.Add(items[i]);
            }
        }
        if (toUpdate.Count > 0)
        {
            await itemRepository.UpdateAsync(toUpdate);
        }
    }

    private static ReadingListDto Map(ReadingList l, int BookCount, CardBannerPreview preview) => new(
        l.Id, l.Name, l.Description, l.OwnerUserId, BookCount, l.CreatedAt, preview);

    private Result ApplyReadingListBannerUpdateAsync(
        int readingListId,
        ReadingList list,
        CardHeaderBannerMode mode,
        IReadOnlyList<int> selectedBookIds,
        HashSet<int> memberBookIds)
    {
        if (mode == CardHeaderBannerMode.UploadedImage && string.IsNullOrEmpty(list.CardBannerImageFileName)
                                                      && storage.FindCardBannerFilePath(CardBannerSupport.KindReadingLists, readingListId) is null)
        {
            return Result.Invalid(new ValidationError(
                nameof(mode),
                "Upload a banner image first, or choose another header option."));
        }

        var normalized = selectedBookIds.Where(memberBookIds.Contains).Take(CardBannerLimits.MaxStripCovers).ToList();
        if (mode == CardHeaderBannerMode.SelectedBooks && normalized.Count == 0)
        {
            return Result.Invalid(new ValidationError(
                nameof(selectedBookIds),
                $"Pick up to {CardBannerLimits.MaxStripCovers} books from this reading list for the header."));
        }

        if (mode != CardHeaderBannerMode.UploadedImage)
        {
            storage.DeleteCardBannerFile(CardBannerSupport.KindReadingLists, readingListId);
            list.CardBannerImageFileName = null;
        }

        list.CardBannerMode = mode;
        list.CardBannerBookIdsJson = mode == CardHeaderBannerMode.SelectedBooks
            ? CardBannerSupport.SerializeBookIds(normalized)
            : null;

        return Result.Success();
    }

    private async Task<Dictionary<int, List<CardBannerSupport.BookCoverSource>>> LoadReadingListBannerSourcesAsync(
        IReadOnlyList<int> listIds,
        CancellationToken cancellationToken)
    {
        if (listIds.Count == 0)
        {
            return [];
        }

        var rows = (await itemRepository.FindAsync(new SearchOptions<ReadingListItem>
        {
            Query = i => listIds.Contains(i.ReadingListId),
            Include = q => q.Include(i => i.Book),
            CancellationToken = cancellationToken,
        })).ToList();

        var dict = new Dictionary<int, List<CardBannerSupport.BookCoverSource>>();
        foreach (var item in rows)
        {
            var b = item.Book;
            if (b is null || string.IsNullOrEmpty(b.CoverImagePath))
            {
                continue;
            }

            if (!dict.TryGetValue(item.ReadingListId, out var list))
            {
                list = [];
                dict[item.ReadingListId] = list;
            }

            list.Add(new CardBannerSupport.BookCoverSource(b.Id));
        }

        return dict;
    }
}