namespace Shelfwarden.Data.Npgsql;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContextBase(options)
{
}
