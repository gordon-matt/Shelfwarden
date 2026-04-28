using Microsoft.AspNetCore.Identity;

namespace Shelfwarden.Data.Entities;

public class ApplicationRole : IdentityRole
{
    public ApplicationRole()
    {
    }

    public ApplicationRole(string roleName)
        : base(roleName)
    {
    }

    public virtual ICollection<ApplicationUser> Users { get; set; } = [];
}