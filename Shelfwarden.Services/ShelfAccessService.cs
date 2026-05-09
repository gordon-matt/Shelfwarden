namespace Shelfwarden.Services;

public class ShelfAccessService(
    IUserContextService userContext,
    IRepository<Shelf> shelfRepository,
    IRepository<Book> bookRepository) : IShelfAccessService
{
    public async Task<IReadOnlySet<int>?> GetAccessibleShelfIdsAsync(CancellationToken cancellationToken = default)
    {
        if (userContext.IsAdministrator())
        {
            return null;
        }

        var shelves = await shelfRepository.FindAsync(new SearchOptions<Shelf>
        {
            Include = q => q
                .Include(s => s.UserAccessEntries)
                .Include(s => s.RoleAccessEntries),
            OrderBy = q => q.OrderBy(s => s.Id),
            CancellationToken = cancellationToken,
        });

        var set = new HashSet<int>();
        foreach (var s in shelves)
        {
            if (ShelfAccessEvaluator.CanAccessShelf(s, userContext))
            {
                set.Add(s.Id);
            }
        }

        return set;
    }

    public async Task<bool> CanAccessShelfAsync(int shelfId, CancellationToken cancellationToken = default)
    {
        if (userContext.IsAdministrator())
        {
            return true;
        }

        var shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = x => x.Id == shelfId,
            Include = q => q
                .Include(s => s.UserAccessEntries)
                .Include(s => s.RoleAccessEntries),
            CancellationToken = cancellationToken,
        });

        return shelf is not null && ShelfAccessEvaluator.CanAccessShelf(shelf, userContext);
    }

    public async Task<bool> CanAccessBookAsync(int bookId, CancellationToken cancellationToken = default)
    {
        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });

        return book is not null && await CanAccessShelfAsync(book.ShelfId, cancellationToken);
    }
}