namespace Shelfwarden.Services;

/// <summary>
/// Resolves which shelves the current user may browse. Administrators see all shelves.
/// When a shelf has no user/role restrictions, every signed-in user may see it.
/// </summary>
public interface IShelfAccessService
{
    /// <summary>
    /// When null, the user may access every shelf (administrator). Otherwise only the listed shelf ids.
    /// </summary>
    Task<IReadOnlySet<int>?> GetAccessibleShelfIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the caller may open this shelf. Administrators always may. A shelf with no user
    /// or role list is open to every signed-in user. A missing shelf is false.
    /// </summary>
    Task<bool> CanAccessShelfAsync(int shelfId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the caller may open the shelf that holds this book. A missing book is false.
    /// </summary>
    Task<bool> CanAccessBookAsync(int bookId, CancellationToken cancellationToken = default);
}