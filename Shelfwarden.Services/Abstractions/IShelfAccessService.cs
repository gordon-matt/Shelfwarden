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

    Task<bool> CanAccessShelfAsync(int shelfId, CancellationToken cancellationToken = default);

    Task<bool> CanAccessBookAsync(int bookId, CancellationToken cancellationToken = default);
}
