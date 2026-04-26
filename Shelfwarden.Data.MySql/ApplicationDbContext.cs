namespace Shelfwarden.Data.MySql;

/// <summary>
/// MySQL <see cref="ApplicationDbContextBase"/> placeholder. The actual UseMySql wiring is
/// disabled until Pomelo.EntityFrameworkCore.MySql ships an EF Core 10 release. See the
/// .csproj for status notes. Falls back to InMemory so the type is constructible (e.g. for
/// design-time tooling) but the registration extension throws on DI to prevent silent misuse.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContextBase(options)
{
}
