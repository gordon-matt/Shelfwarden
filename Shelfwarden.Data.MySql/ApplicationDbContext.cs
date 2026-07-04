namespace Shelfwarden.Data.MySql;

/// <summary>
/// MySQL/MariaDB-flavoured <see cref="ApplicationDbContextBase"/>. Migrations for MySQL live in
/// this assembly so that <c>dotnet ef</c> only ever sees one provider per project.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContextBase(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // MySQL has no concept of a schema that is distinct from a database, so the shared
        // HasDefaultSchema("app") + per-table "app" schema would make EF target a database
        // literally named "app". Flatten everything back into the connection's own database.
        builder.Model.SetDefaultSchema(null);
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            entityType.SetSchema(null);
        }

        // InnoDB caps an index key at 3072 bytes; with utf8mb4 (4 bytes/char) that is 768
        // characters, and a composite index shares that single budget. FilePath is a 1024-char
        // unicode column that participates in indexes (Books: ShelfId + FilePath; and a unique
        // index on AdditionalContentItems.FilePath), so at full width the key overflows the limit
        // and CREATE INDEX fails with "Specified key was too long". Oracle's MySQL provider has no
        // prefix-index support (unlike Pomelo's HasPrefixLength), so we shorten the indexed path
        // columns just enough that the keys fit. 760 chars is still far beyond any realistic file
        // path and leaves headroom for the composite index's leading int column.
        builder.Entity<Book>().Property(b => b.FilePath).HasMaxLength(760);
        builder.Entity<AdditionalContentItem>().Property(a => a.FilePath).HasMaxLength(760);
    }
}
