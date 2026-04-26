using Microsoft.AspNetCore.Identity;

namespace Shelfwarden.Data.Entities;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<ApplicationRole> Roles { get; set; } = [];
}
