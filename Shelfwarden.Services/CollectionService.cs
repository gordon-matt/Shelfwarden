using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services;

public class CollectionService(
    IUserContextService userContext,
    IRepository<Collection> collectionRepository,
    IRepository<CollectionBook> collectionBookRepository,
    IRepository<Book> bookRepository,
    IRepository<BookProgress> progressRepository,
    IStoragePathProvider storage) : ICollectionService
{
    public async Task<Result<IReadOnlyList<CollectionDto>>> ListAsync(int? shelfId = null, CancellationToken cancellationToken = default)
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
                var chunk = (await collectionBookRepository.FindAsync(
                    new SearchOptions<CollectionBook>
                    {
                        Query = cb => cb.Book.ShelfId == sid,
                        PageNumber = page,
                        PageSize = pageSize,
                        CancellationToken = cancellationToken,
                    },
                    cb => cb.CollectionId)).ToList();

                if (chunk.Count == 0)
                {
                    break;
                }

                foreach (int collectionId in chunk)
                {
                    onShelf.Add(collectionId);
                }

                if (chunk.Count < pageSize)
                {
                    break;
                }

                page++;
            }
        }

        // Fetch personal + global in a single query so we can build the result without
        // round-trips. The unique index `(OwnerUserId, Name)` is per-owner so the same name
        // can collide between a user's list and a global list — that's fine, the UI shows
        // a "global" badge to disambiguate.
        var rows = (await collectionRepository.FindAsync(new SearchOptions<Collection>
        {
            Query = c => c.OwnerUserId == userId || c.OwnerUserId == Constants.GlobalUserId,
            OrderBy = q => q.OrderByDescending(c => c.OwnerUserId == Constants.GlobalUserId).ThenBy(c => c.Name),
            CancellationToken = cancellationToken,
        })).ToList();

        if (onShelf is not null)
        {
            rows = rows.Where(c => onShelf.Contains(c.Id)).ToList();
        }

        var ids = rows.Select(c => c.Id).ToList();
        var counts = (await collectionBookRepository.FindAsync(
                new SearchOptions<CollectionBook> { Query = cb => ids.Contains(cb.CollectionId) },
                cb => cb.CollectionId))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        var bannerSources = await LoadCollectionBannerSourcesAsync(ids, cancellationToken);

        IReadOnlyList<CollectionDto> dtos = rows
            .Select(c =>
            {
                var preview = CardBannerSupport.BuildPreview(
                    c.CardBannerMode,
                    c.CardBannerImageFileName,
                    c.CardBannerBookIdsJson,
                    CardBannerSupport.KindCollections,
                    c.Id,
                    bannerSources.GetValueOrDefault(c.Id) ?? [],
                    storage);
                return Map(c, counts.GetValueOrDefault(c.Id, 0), preview);
            })
            .ToList();
        return Result.Success(dtos);
    }

    public async Task<Result<CollectionDetailDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var collection = await collectionRepository.FindOneAsync(new SearchOptions<Collection>
        {
            Query = c => c.Id == id,
            CancellationToken = cancellationToken,
        });
        if (collection is null)
        {
            return Result.NotFound();
        }

        if (!CanRead(collection, userId))
        {
            return Result.Forbidden();
        }

        var bookIds = (await collectionBookRepository.FindAsync(
                new SearchOptions<CollectionBook> { Query = cb => cb.CollectionId == id },
                cb => cb.BookId))
            .ToList();

        var books = bookIds.Count == 0
            ? []
            : (await bookRepository.FindAsync(new SearchOptions<Book>
            {
                Query = b => bookIds.Contains(b.Id),
                Include = q => q.Include(b => b.Series).Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
                OrderBy = q => q.OrderBy(b => b.SortTitle ?? b.Title),
                SplitQuery = true,
                CancellationToken = cancellationToken,
            })).ToList();

        var progress = await BookProjections.LoadProgressPercentagesAsync(
            progressRepository, userId, books.Select(b => b.Id).ToList(), cancellationToken);

        var items = books
            .Select(b => BookProjections.ToListItem(b, progress.GetValueOrDefault(b.Id, 0)))
            .ToList();

        var settings = CardBannerSupport.BuildSettings(
            collection.CardBannerMode,
            collection.CardBannerImageFileName,
            collection.CardBannerBookIdsJson,
            CardBannerSupport.KindCollections,
            collection.Id,
            storage);

        return Result.Success(new CollectionDetailDto(
            collection.Id, collection.Name, collection.Description,
            collection.OwnerUserId, IsGlobal(collection), collection.CreatedAt, items, settings));
    }

    public async Task<Result<CollectionDto>> CreateAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        // Only admins can create global collections; everyone else gets a personal one.
        bool wantsGlobal = request.IsGlobal && userContext.IsAdministrator();
        string ownerId = wantsGlobal ? Constants.GlobalUserId : userId;
        string trimmedName = request.Name.Trim();

        var clash = await collectionRepository.FindOneAsync(new SearchOptions<Collection>
        {
            Query = c => c.OwnerUserId == ownerId && c.Name == trimmedName,
            CancellationToken = cancellationToken,
        });
        if (clash is not null)
        {
            return Result.Conflict($"You already have a collection called \"{trimmedName}\".");
        }

        var inserted = await collectionRepository.InsertAsync(new Collection
        {
            Name = trimmedName,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            OwnerUserId = ownerId,
            CreatedAt = DateTime.UtcNow,
        });

        var preview = CardBannerSupport.BuildPreview(
            CardHeaderBannerMode.RandomCovers,
            null,
            null,
            CardBannerSupport.KindCollections,
            inserted.Id,
            [],
            storage);

        return Result.Success(Map(inserted, BookCount: 0, preview));
    }

    public async Task<Result<CollectionDto>> UpdateAsync(int id, UpdateCollectionRequest request, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var collection = await collectionRepository.FindOneAsync(new SearchOptions<Collection>
        {
            Query = c => c.Id == id,
            CancellationToken = cancellationToken,
        });
        if (collection is null)
        {
            return Result.NotFound();
        }

        if (!CanModify(collection, userId))
        {
            return Result.Forbidden();
        }

        bool wasGlobal = IsGlobal(collection);
        if (request.IsGlobal != wasGlobal && !userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        string trimmedName = request.Name.Trim();
        string targetOwnerId = collection.OwnerUserId;
        if (request.IsGlobal != wasGlobal)
        {
            targetOwnerId = request.IsGlobal ? Constants.GlobalUserId : userId!;
        }

        var nameClash = await collectionRepository.FindOneAsync(new SearchOptions<Collection>
        {
            Query = c => c.OwnerUserId == targetOwnerId && c.Name == trimmedName && c.Id != id,
            CancellationToken = cancellationToken,
        });
        if (nameClash is not null)
        {
            return Result.Conflict($"A collection named \"{trimmedName}\" already exists in that scope.");
        }

        collection.OwnerUserId = targetOwnerId;
        collection.Name = trimmedName;
        collection.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        var memberIds = (await collectionBookRepository.FindAsync(
            new SearchOptions<CollectionBook> { Query = cb => cb.CollectionId == id },
            cb => cb.BookId)).ToHashSet();

        Result bannerResult = ApplyCollectionBannerUpdate(
            id, collection, request.CardBannerMode, request.CardBannerSelectedBookIds, memberIds);
        if (!bannerResult.IsSuccess)
        {
            return Result<CollectionDto>.Invalid(bannerResult.ValidationErrors);
        }

        var updated = await collectionRepository.UpdateAsync(collection);

        int count = await collectionBookRepository.CountAsync(cb => cb.CollectionId == id);

        var bannerSources = await LoadCollectionBannerSourcesAsync([id], cancellationToken);
        var preview = CardBannerSupport.BuildPreview(
            updated.CardBannerMode,
            updated.CardBannerImageFileName,
            updated.CardBannerBookIdsJson,
            CardBannerSupport.KindCollections,
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

        var collection = await collectionRepository.FindOneAsync(new SearchOptions<Collection>
        {
            Query = c => c.Id == id,
            CancellationToken = cancellationToken,
        });

        if (collection is null)
        {
            return Result.NotFound();
        }

        if (!CanModify(collection, userId))
        {
            return Result.Forbidden();
        }

        storage.DeleteCardBannerFile(CardBannerSupport.KindCollections, id);

        // Cascade delete handles CollectionBook rows on the database side (configured in
        // CollectionBookMap). Just drop the parent.
        await collectionRepository.DeleteAsync(collection);
        return Result.Success();
    }

    public async Task<Result> AddBookAsync(int collectionId, int bookId, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var collection = await collectionRepository.FindOneAsync(new SearchOptions<Collection>
        {
            Query = c => c.Id == collectionId,
            CancellationToken = cancellationToken,
        });
        if (collection is null)
        {
            return Result.NotFound("Collection not found.");
        }

        if (!CanModify(collection, userId))
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

        var existing = await collectionBookRepository.FindOneAsync(new SearchOptions<CollectionBook>
        {
            Query = cb => cb.CollectionId == collectionId && cb.BookId == bookId,
            CancellationToken = cancellationToken,
        });
        if (existing is not null)
        {
            // Idempotent — already added. Treat as success so re-clicking "Add" is harmless.
            return Result.Success();
        }

        await collectionBookRepository.InsertAsync(new CollectionBook
        {
            CollectionId = collectionId,
            BookId = bookId,
        });
        return Result.Success();
    }

    public async Task<Result> RemoveBookAsync(int collectionId, int bookId, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var collection = await collectionRepository.FindOneAsync(new SearchOptions<Collection>
        {
            Query = c => c.Id == collectionId,
            CancellationToken = cancellationToken,
        });
        if (collection is null)
        {
            return Result.NotFound();
        }

        if (!CanModify(collection, userId))
        {
            return Result.Forbidden();
        }

        var entry = await collectionBookRepository.FindOneAsync(new SearchOptions<CollectionBook>
        {
            Query = cb => cb.CollectionId == collectionId && cb.BookId == bookId,
            CancellationToken = cancellationToken,
        });
        if (entry is null)
        {
            return Result.NotFound();
        }

        await collectionBookRepository.DeleteAsync(entry);
        return Result.Success();
    }

    private bool CanRead(Collection collection, string userId)
        => IsGlobal(collection) || collection.OwnerUserId == userId;

    private bool CanModify(Collection collection, string userId)
        => IsGlobal(collection) ? userContext.IsAdministrator() : collection.OwnerUserId == userId;

    private static bool IsGlobal(Collection c) => c.OwnerUserId == Constants.GlobalUserId;

    private static CollectionDto Map(Collection c, int BookCount, CardBannerPreview preview) => new(
        c.Id, c.Name, c.Description, c.OwnerUserId, IsGlobal(c), BookCount, c.CreatedAt, preview);

    public async Task<Result> UploadCardBannerAsync(
        int collectionId,
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

        var collection = await collectionRepository.FindOneAsync(new SearchOptions<Collection>
        {
            Query = c => c.Id == collectionId,
            CancellationToken = cancellationToken,
        });
        if (collection is null)
        {
            return Result.NotFound();
        }

        if (!CanModify(collection, userId))
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
            CardBannerSupport.KindCollections,
            collectionId,
            ms,
            fileName,
            cancellationToken);

        collection.CardBannerMode = CardHeaderBannerMode.UploadedImage;
        collection.CardBannerImageFileName = relative;
        collection.CardBannerBookIdsJson = null;
        await collectionRepository.UpdateAsync(collection);

        return Result.Success();
    }

    private Result ApplyCollectionBannerUpdate(
        int collectionId,
        Collection collection,
        CardHeaderBannerMode mode,
        IReadOnlyList<int> selectedBookIds,
        HashSet<int> memberBookIds)
    {
        if (mode == CardHeaderBannerMode.UploadedImage &&
            string.IsNullOrEmpty(collection.CardBannerImageFileName) &&
            storage.FindCardBannerFilePath(CardBannerSupport.KindCollections, collectionId) is null)
        {
            return Result.Invalid(new ValidationError(
                nameof(mode),
                "Upload a banner image first, or choose another header option."));
        }

        var normalized = selectedBookIds.Where(memberBookIds.Contains).Take(5).ToList();
        if (mode == CardHeaderBannerMode.SelectedBooks && normalized.Count == 0)
        {
            return Result.Invalid(new ValidationError(
                nameof(selectedBookIds),
                "Pick up to five books from this collection for the header."));
        }

        if (mode != CardHeaderBannerMode.UploadedImage)
        {
            storage.DeleteCardBannerFile(CardBannerSupport.KindCollections, collectionId);
            collection.CardBannerImageFileName = null;
        }

        collection.CardBannerMode = mode;
        collection.CardBannerBookIdsJson = mode == CardHeaderBannerMode.SelectedBooks
            ? CardBannerSupport.SerializeBookIds(normalized)
            : null;

        return Result.Success();
    }

    private async Task<Dictionary<int, List<CardBannerSupport.BookCoverSource>>> LoadCollectionBannerSourcesAsync(
        IReadOnlyList<int> collectionIds,
        CancellationToken cancellationToken)
    {
        if (collectionIds.Count == 0)
        {
            return [];
        }

        var rows = (await collectionBookRepository.FindAsync(new SearchOptions<CollectionBook>
        {
            Query = cb => collectionIds.Contains(cb.CollectionId),
            Include = q => q.Include(cb => cb.Book),
            CancellationToken = cancellationToken,
        })).ToList();

        var dict = new Dictionary<int, List<CardBannerSupport.BookCoverSource>>();
        foreach (CollectionBook cb in rows)
        {
            Book? b = cb.Book;
            if (b is null || string.IsNullOrEmpty(b.CoverImagePath))
            {
                continue;
            }

            long v = BookCoverCaching.GetCoverCacheVersion(b.LastScannedAt, b.UpdatedAt, b.CreatedAt);
            if (!dict.TryGetValue(cb.CollectionId, out List<CardBannerSupport.BookCoverSource>? list))
            {
                list = [];
                dict[cb.CollectionId] = list;
            }

            list.Add(new CardBannerSupport.BookCoverSource(b.Id, v));
        }

        return dict;
    }
}