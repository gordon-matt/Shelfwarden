using Microsoft.Extensions.DependencyInjection;

namespace Shelfwarden.Infrastructure;

public static class RepositoryRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>Open generic registration for <see cref="IRepository{T}"/> using Extenso's EF impl.</summary>
        public IServiceCollection AddShelfwardenRepositories()
        {
            services.AddEntityFrameworkRepository();
            return services;
        }
    }
}
