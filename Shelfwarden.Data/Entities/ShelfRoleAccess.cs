namespace Shelfwarden.Data.Entities;

/// <summary>Grants everyone in a role access to a shelf when the shelf has restricted visibility.</summary>
public class ShelfRoleAccess : BaseEntity<int>
{
    public int ShelfId { get; set; }

    /// <summary>Upper-invariant role name for comparison with role claims (e.g. USER, ADMINISTRATOR).</summary>
    public required string NormalizedRoleName { get; set; }

    public virtual Shelf Shelf { get; set; } = null!;
}

public class ShelfRoleAccessMap : IEntityTypeConfiguration<ShelfRoleAccess>
{
    public void Configure(EntityTypeBuilder<ShelfRoleAccess> builder)
    {
        builder.ToTable("ShelfRoleAccess", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.NormalizedRoleName).IsRequired().HasMaxLength(256);

        builder.HasIndex(m => new { m.ShelfId, m.NormalizedRoleName }).IsUnique();

        builder.HasOne(m => m.Shelf)
            .WithMany(s => s.RoleAccessEntries)
            .HasForeignKey(m => m.ShelfId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}