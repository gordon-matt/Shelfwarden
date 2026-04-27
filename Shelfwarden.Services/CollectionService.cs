namespace Shelfwarden.Services;

public class CollectionService(
    IUserContextService userContext,
    IRepository<Collection> collectionRepository,
    IRepository<CollectionBook> collectionBookRepository,
    IRepository<Book> bookRepository,
    IRepository<BookProgress> progressRepository) : ICollectionService
{
    public async Task<Result<IReadOnlyList<CollectionDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        // Fetch personal + global in a single query so we can build the result without
        // round-trips. The unique index `(OwnerUserId, Name)` is per-owner so the same name
        // can collide between a user's list and a global list — that's fine, the UI shows
        // a "global" badge to disambiguate.
        var rows = await collectionRepository.FindAsync(new SearchOptions<Collection>
        {
            Query = c => c.OwnerUserId == userId || c.OwnerUserId == Constants.GlobalUserId,
            OrderBy = q => q.OrderByDescending(c => c.OwnerUserId == Constants.GlobalUserId).ThenBy(c => c.Name),
            CancellationToken = cancellationToken,
        });

        var ids = rows.Select(c => c.Id).ToList();
        var counts = (await collectionBookRepository.FindAsync(
                new SearchOptions<CollectionBook> { Query = cb => ids.Contains(cb.CollectionId) },
                cb => cb.CollectionId))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        IReadOnlyList<CollectionDto> dtos = rows.Select(c => Map(c, counts.GetValueOrDefault(c.Id, 0))).ToList();
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
        if (collection is null) return Result.NotFound();

        if (!CanRead(collection, userId)) return Result.Forbidden();

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

        return Result.Success(new CollectionDetailDto(
            collection.Id, collection.Name, collection.Description,
            collection.OwnerUserId, IsGlobal(collection), collection.CreatedAt, items));
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
        return Result.Success(Map(inserted, BookCount: 0));
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
        if (collection is null) return Result.NotFound();
        if (!CanModify(collection, userId)) return Result.Forbidden();

        collection.Name = request.Name.Trim();
        collection.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var updated = await collectionRepository.UpdateAsync(collection);

        int count = (await collectionBookRepository.FindAsync(
                new SearchOptions<CollectionBook> { Query = cb => cb.CollectionId == id },
                cb => cb.Id))
            .Count();

        return Result.Success(Map(updated, count));
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
        if (collection is null) return Result.NotFound();
        if (!CanModify(collection, userId)) return Result.Forbidden();

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
        if (collection is null) return Result.NotFound("Collection not found.");
        if (!CanModify(collection, userId)) return Result.Forbidden();

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });
        if (book is null) return Result.NotFound("Book not found.");

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
        if (collection is null) return Result.NotFound();
        if (!CanModify(collection, userId)) return Result.Forbidden();

        var entry = await collectionBookRepository.FindOneAsync(new SearchOptions<CollectionBook>
        {
            Query = cb => cb.CollectionId == collectionId && cb.BookId == bookId,
            CancellationToken = cancellationToken,
        });
        if (entry is null) return Result.NotFound();

        await collectionBookRepository.DeleteAsync(entry);
        return Result.Success();
    }

    private bool CanRead(Collection collection, string userId)
        => IsGlobal(collection) || collection.OwnerUserId == userId;

    private bool CanModify(Collection collection, string userId)
        => IsGlobal(collection) ? userContext.IsAdministrator() : collection.OwnerUserId == userId;

    private static bool IsGlobal(Collection c) => c.OwnerUserId == Constants.GlobalUserId;

    private static CollectionDto Map(Collection c, int BookCount) => new(
        c.Id, c.Name, c.Description, c.OwnerUserId, IsGlobal(c), BookCount, c.CreatedAt);
}
