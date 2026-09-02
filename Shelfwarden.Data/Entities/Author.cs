namespace Shelfwarden.Data.Entities;

public class Author : BaseEntity<int>
{
    public required string Name { get; set; }

    /// <summary>Lowercased / normalized version of <see cref="Name"/> for case-insensitive lookups.</summary>
    public required string NormalizedName { get; set; }

    public string? Biography { get; set; }

    public int? PrimaryAuthorId { get; set; }

    public virtual Author? PrimaryAuthor { get; set; } = null;

    public virtual ICollection<Author> Pseudonyms { get; set; } = [];

    public virtual ICollection<BookAuthor> BookAuthors { get; set; } = [];

    public virtual ICollection<AdditionalContentItem> AdditionalContent { get; set; } = [];

    public virtual ICollection<AuthorLink> Links { get; set; } = [];
}

public class AuthorMap : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> builder)
    {
        builder.ToTable("Authors", Constants.Schemas.App);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.NormalizedName).IsRequired().HasMaxLength(256).IsUnicode(true);
        builder.Property(m => m.Biography).IsUnicode(true);

        builder.HasIndex(m => m.NormalizedName).IsUnique();

        builder.HasOne(a => a.PrimaryAuthor)
           .WithMany(a => a.Pseudonyms)
           .HasForeignKey(a => a.PrimaryAuthorId)
           .IsRequired(false)
           .OnDelete(DeleteBehavior.Restrict); // prevent cascade delete of pseudonyms
    }
}