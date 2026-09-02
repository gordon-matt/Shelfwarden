using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shelfwarden.Data.Sqlite;

namespace Shelfwarden.Tests.Infrastructure;

/// <summary>
/// Spins up the SQLite-flavoured <see cref="ApplicationDbContext"/> against a real (in-memory)
/// SQLite database rather than EF Core's in-memory provider.
/// <para>
/// SQLite is a genuine relational engine, so tests exercise foreign keys, unique indexes and the
/// <c>ExecuteUpdate</c> / <c>ExecuteDelete</c> bulk operations the services rely on — none of
/// which EF's in-memory provider supports. It's also the default provider in production, so the
/// tests run against the engine most installs actually use.
/// </para>
/// <para>
/// The database lives in shared-cache memory under a name unique to this fixture. SQLite drops an
/// in-memory database when its last connection closes, so <see cref="_keepAlive"/> is held open
/// for the lifetime of the fixture to stop EF's connection pooling from wiping it between calls.
/// </para>
/// </summary>
public sealed class TestDbFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;

    public TestDbFixture()
    {
        string connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
            })
            .Build();

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

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }
}
