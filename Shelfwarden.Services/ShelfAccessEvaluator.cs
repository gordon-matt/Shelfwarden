using Shelfwarden.Data.Entities;
using Shelfwarden.Services.Auth;

namespace Shelfwarden.Services;

internal static class ShelfAccessEvaluator
{
    public static bool CanAccessShelf(Shelf shelf, IUserContextService userContext)
    {
        if (userContext.IsAdministrator())
        {
            return true;
        }

        bool restricted = shelf.UserAccessEntries.Count > 0 || shelf.RoleAccessEntries.Count > 0;
        if (!restricted)
        {
            return true;
        }

        string? uid = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(uid))
        {
            return false;
        }

        if (shelf.UserAccessEntries.Any(e => e.UserId == uid))
        {
            return true;
        }

        IReadOnlyList<string> userRoles = userContext.GetRoleNames();
        foreach (ShelfRoleAccess ra in shelf.RoleAccessEntries)
        {
            foreach (string role in userRoles)
            {
                if (string.Equals(role, ra.NormalizedRoleName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
