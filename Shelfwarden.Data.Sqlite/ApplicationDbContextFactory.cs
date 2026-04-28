using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace Shelfwarden.Data.Sqlite;

public class ApplicationDbContextFactory(IConfiguration configuration) : IDbContextFactory
{
    private DbContextOptions<ApplicationDbContext> Options
        => field ??= BuildOptions(configuration.GetConnectionString("DefaultConnection"));

    public DbContext GetContext() => new ApplicationDbContext(Options);

    public DbContext GetContext(string connectionString)
        => new ApplicationDbContext(BuildOptions(connectionString));

    private static DbContextOptions<ApplicationDbContext> BuildOptions(string? connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        if (string.IsNullOrEmpty(connectionString))
        {
            optionsBuilder.UseInMemoryDatabase("ShelfwardenDb");
        }
        else
        {
            optionsBuilder.UseSqlite(connectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
            // See ServiceCollectionExtensions for context — silences SQLite-only schema warnings.
            optionsBuilder.ConfigureWarnings(w => w.Ignore(SqliteEventId.SchemaConfiguredWarning));
        }

        return optionsBuilder.Options;
    }
}