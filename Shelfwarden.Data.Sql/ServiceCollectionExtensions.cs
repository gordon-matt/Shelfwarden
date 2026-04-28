using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shelfwarden.Data.Sql;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddShelfwardenSqlServer(IConfiguration configuration)
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
                    options.UseSqlServer(connectionString, sql =>
                        sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));
                }
            });

            services.AddScoped<ApplicationDbContextBase>(sp => sp.GetRequiredService<ApplicationDbContext>());
            services.AddSingleton<IDbContextFactory, ApplicationDbContextFactory>();

            return services;
        }

        public IServiceCollection AddShelfwardenSqlServerHangfire(string connectionString)
        {
            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                {
                    QueuePollInterval = TimeSpan.FromSeconds(15),
                    UseRecommendedIsolationLevel = true,
                    DisableGlobalLocks = true,
                }));

            return services;
        }
    }
}