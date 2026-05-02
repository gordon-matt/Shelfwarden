using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shelfwarden.Data.Entities;

/// <summary>Grants a specific user access to a shelf when the shelf has restricted visibility.</summary>
public class ShelfUserAccess : BaseEntity<int>
{
    public int ShelfId { get; set; }

    /// <summary>ASP.NET Identity user id (or Keycloak subject id).</summary>
    public required string UserId { get; set; }

    public virtual Shelf Shelf { get; set; } = null!;
}

public class ShelfUserAccessMap : IEntityTypeConfiguration<ShelfUserAccess>
{
    public void Configure(EntityTypeBuilder<ShelfUserAccess> builder)
    {
        builder.ToTable("ShelfUserAccess", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.UserId).IsRequired().HasMaxLength(450);

        builder.HasIndex(m => new { m.ShelfId, m.UserId }).IsUnique();

        builder.HasOne(m => m.Shelf)
            .WithMany(s => s.UserAccessEntries)
            .HasForeignKey(m => m.ShelfId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
