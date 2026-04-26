using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shelfwarden.Data.MySql;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Throws — MySQL is structurally wired but disabled until Pomelo ships an EF Core 10
        /// release. The web project's provider switch is expected to surface this error during
        /// startup, not at first request.
        /// </summary>
        public IServiceCollection AddShelfwardenMySql(IConfiguration configuration)
            => throw new NotSupportedException(
                "MySQL provider is not currently supported. " +
                "Pomelo.EntityFrameworkCore.MySql has not yet released an EF Core 10 build.");

        public IServiceCollection AddShelfwardenMySqlHangfire(string connectionString)
            => throw new NotSupportedException(
                "MySQL Hangfire storage is not currently wired up. Re-enable when " +
                "Hangfire.Storage.MySql / Pomelo are confirmed working on EF Core 10.");
    }
}
