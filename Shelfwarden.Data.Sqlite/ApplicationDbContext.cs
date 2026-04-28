namespace Shelfwarden.Data.Sqlite;

/// <summary>
/// SQLite-flavoured <see cref="ApplicationDbContextBase"/>. Migrations for SQLite live in
/// this assembly so that <c>dotnet ef</c> only ever sees one provider per project.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContextBase(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // SQLite doesn't have decimals — store NumberInSeries as REAL (double) to keep ordering correct.
        // Using HasConversion preserves the C# type while telling EF to round-trip via double.
        builder.Entity<Book>()
            .Property(b => b.NumberInSeries)
            .HasConversion<double?>();
    }
}