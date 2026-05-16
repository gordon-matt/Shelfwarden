using Microsoft.EntityFrameworkCore.Design;

namespace Shelfwarden.Data.Npgsql;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling so migrations can be generated without
/// needing a running PostgreSQL instance or a fully wired-up host application.
/// </summary>
public class ApplicationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=shelfwarden_design;Username=postgres;Password=design",
            npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
