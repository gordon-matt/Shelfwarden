using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Shelfwarden.Data;

/// <summary>
/// Provider-agnostic <see cref="DbContext"/> shared by all <c>Shelfwarden.Data.{Provider}</c>
/// projects. Each provider project inherits from this class and supplies its own
/// <c>OnConfiguring</c> / migrations assembly. All <see cref="IEntityTypeConfiguration{TEntity}"/>
/// classes that live next to the entities in this assembly are picked up automatically.
/// Provider-specific overrides (e.g. column types, JSON columns) can be applied by overriding
/// <see cref="OnModelCreating"/> in the subclass and calling <c>base.OnModelCreating(builder)</c>.
/// </summary>
public abstract class ApplicationDbContextBase
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    protected ApplicationDbContextBase(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Shelf> Shelves => Set<Shelf>();

    public DbSet<ShelfFolder> ShelfFolders => Set<ShelfFolder>();

    public DbSet<ShelfUserAccess> ShelfUserAccess => Set<ShelfUserAccess>();

    public DbSet<ShelfRoleAccess> ShelfRoleAccess => Set<ShelfRoleAccess>();

    public DbSet<Book> Books => Set<Book>();

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<BookAuthor> BookAuthors => Set<BookAuthor>();

    public DbSet<Series> Series => Set<Series>();

    public DbSet<Genre> Genres => Set<Genre>();

    public DbSet<BookGenre> BookGenres => Set<BookGenre>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<BookTag> BookTags => Set<BookTag>();

    public DbSet<BookProgress> BookProgress => Set<BookProgress>();

    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    public DbSet<BookUser> BookUsers => Set<BookUser>();

    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<CollectionBook> CollectionBooks => Set<CollectionBook>();

    public DbSet<ReadingList> ReadingLists => Set<ReadingList>();

    public DbSet<ReadingListItem> ReadingListItems => Set<ReadingListItem>();

    public DbSet<ServerSetting> ServerSettings => Set<ServerSetting>();

    public DbSet<Audiobook> Audiobooks => Set<Audiobook>();

    public DbSet<AdditionalContentItem> AdditionalContentItems => Set<AdditionalContentItem>();

    public DbSet<BookAdditionalContentItem> BookAdditionalContents => Set<BookAdditionalContentItem>();

    public DbSet<SeriesAdditionalContentItem> SeriesAdditionalContents => Set<SeriesAdditionalContentItem>();

    public DbSet<AdditionalContentTag> AdditionalContentTags => Set<AdditionalContentTag>();

    public DbSet<AdditionalContentItemTag> AdditionalContentItemTags => Set<AdditionalContentItemTag>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Constants.Schemas.App);

        // Pick up every IEntityTypeConfiguration<T> from this assembly (entities live alongside their maps).
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContextBase).Assembly);

        ConfigureIdentityTables(builder);
    }

    /// <summary>
    /// Keep the ASP.NET Identity tables in the same <c>app</c> schema and rename them so they
    /// don't collide with the <c>AspNet*</c> default names (which look out of place next to ours).
    /// Called from <see cref="OnModelCreating"/>; subclasses don't normally need to touch this.
    /// </summary>
    protected static void ConfigureIdentityTables(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>().ToTable("Users", Constants.Schemas.App);
        builder.Entity<ApplicationRole>().ToTable("Roles", Constants.Schemas.App);

        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>()
            .ToTable("UserRoles", Constants.Schemas.App);

        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<string>>()
            .ToTable("UserClaims", Constants.Schemas.App);

        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>()
            .ToTable("UserLogins", Constants.Schemas.App);

        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>()
            .ToTable("UserTokens", Constants.Schemas.App);

        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>>()
            .ToTable("RoleClaims", Constants.Schemas.App);
    }
}