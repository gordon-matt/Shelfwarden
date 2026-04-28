namespace Shelfwarden.Data.Sql;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : ApplicationDbContextBase(options)
{
}