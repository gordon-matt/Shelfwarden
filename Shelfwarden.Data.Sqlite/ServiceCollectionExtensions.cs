using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shelfwarden.Data.Sqlite;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the SQLite <see cref="ApplicationDbContext"/>, an <see cref="IDbContextFactory"/>
        /// and the matching Hangfire SQLite storage. Falls back to EF Core's in-memory provider when
        /// no connection string is configured (useful for first-run / smoke tests).
        /// </summary>
        public IServiceCollection AddShelfwardenSqlite(IConfiguration configuration)
        {
            string? connectionString = configuration.GetConnectionString("DefaultConnection");

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                if (string.IsNullOrEmpty(connectionString))
                {
                    options.UseInMemoryDatabase("ShelfwardenDb");
                }
                else
                {
                    options.UseSqlite(connectionString, sqlite =>
                        sqlite.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
                    // SQLite ignores HasDefaultSchema("app"); the warning is correct but noisy.
                    // Other providers still honour the schema, which is what we want.
                    options.ConfigureWarnings(w => w.Ignore(SqliteEventId.SchemaConfiguredWarning));
                }
            });

            services.AddScoped<ApplicationDbContextBase>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddSingleton<IDbContextFactory, ApplicationDbContextFactory>();

            return services;
        }

        /// <summary>
        /// Registers Hangfire with SQLite-backed storage. Hangfire uses a separate SQLite file so
        /// that frequent job-state writes don't contend with application data.
        /// </summary>
        public IServiceCollection AddShelfwardenSqliteHangfire(string? hangfireDbPath = null)
        {
            string path = hangfireDbPath ?? "hangfire.db";

            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSQLiteStorage(path));

            return services;
        }
    }
}
