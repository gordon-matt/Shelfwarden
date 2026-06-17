namespace Shelfwarden.Data.Entities;

public class AuthorLink : BaseEntity<int>
{
    public int AuthorId { get; set; }

    public string? Name { get; set; }

    public string Url { get; set; } = null!;

    public virtual Author Author { get; set; } = null!;
}

public class AuthorLinkMap : IEntityTypeConfiguration<AuthorLink>
{
    public void Configure(EntityTypeBuilder<AuthorLink> builder)
    {
        builder.ToTable("AuthorLinks", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).HasMaxLength(256);
        builder.Property(m => m.Url).IsRequired().HasMaxLength(2048);

        builder.HasOne(al => al.Author)
            .WithMany(a => a.Links)
            .HasForeignKey(al => al.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}