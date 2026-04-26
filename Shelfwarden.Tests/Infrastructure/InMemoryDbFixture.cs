using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shelfwarden.Data.Sqlite;

namespace Shelfwarden.Tests.Infrastructure;

/// <summary>
/// Spins up the SQLite-flavoured <see cref="ApplicationDbContext"/> backed by EF Core's
/// in-memory provider. The SQLite provider extension automatically falls back to in-memory
/// when no <c>DefaultConnection</c> is configured, so we get a working repository graph
/// without touching the file system.
/// </summary>
public sealed class InMemoryDbFixture : IDisposable
{
    private readonly ServiceProvider _provider;

    public InMemoryDbFixture()
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddShelfwardenSqlite(configuration);
        services.AddEntityFrameworkRepository();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
    }

    public IServiceScope CreateScope() => _provider.CreateScope();

    public void Dispose() => _provider.Dispose();
}
