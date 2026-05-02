using Shelfwarden.Data.Entities;
using Shelfwarden.Services.Auth;

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
        foreach (Shelf s in shelves)
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

        Shelf? shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = x => x.Id == shelfId,
            Include = q => q
                .Include(s => s.UserAccessEntries)
                .Include(s => s.RoleAccessEntries),
            CancellationToken = cancellationToken,
        });

        if (shelf is null)
        {
            return false;
        }

        return ShelfAccessEvaluator.CanAccessShelf(shelf, userContext);
    }

    public async Task<bool> CanAccessBookAsync(int bookId, CancellationToken cancellationToken = default)
    {
        Book? book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });

        if (book is null)
        {
            return false;
        }

        return await CanAccessShelfAsync(book.ShelfId, cancellationToken);
    }
}
