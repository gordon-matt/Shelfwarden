using Microsoft.Extensions.Configuration;

namespace Shelfwarden.Data.MySql;

public class ApplicationDbContextFactory(IConfiguration configuration) : IDbContextFactory
{
    public DbContext GetContext()
        => throw NotSupported();

    public DbContext GetContext(string connectionString)
        => throw NotSupported();

    private static NotSupportedException NotSupported() => new(
        "MySQL provider is not currently supported. Pomelo.EntityFrameworkCore.MySql has " +
        "not yet released an EF Core 10 compatible package. Use Sqlite, Npgsql, or SqlServer " +
        "for now, or downgrade the solution to EF Core 9.");
}
