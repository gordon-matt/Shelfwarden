using Microsoft.Extensions.Configuration;

namespace Shelfwarden.Data.Sqlite;

public class ApplicationDbContextFactory(IConfiguration configuration) : IDbContextFactory
{
    private DbContextOptions<ApplicationDbContext>? _options;

    private DbContextOptions<ApplicationDbContext> Options
        => _options ??= BuildOptions(configuration.GetConnectionString("DefaultConnection"));

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
        }

        return optionsBuilder.Options;
    }
}
