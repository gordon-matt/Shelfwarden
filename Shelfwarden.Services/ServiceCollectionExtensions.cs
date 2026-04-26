using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Shelfwarden.Services;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers all Shelfwarden application services. Auth-provider-specific user-info
        /// implementations are registered separately by the web project, since they depend on
        /// configuration the services library shouldn't know about.
        /// </summary>
        public IServiceCollection AddShelfwardenServices()
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IUserContextService, UserContextService>();

            services.AddScoped<ILibraryService, LibraryService>();
            services.AddScoped<IBookService, BookService>();
            services.AddScoped<IAuthorService, AuthorService>();
            services.AddScoped<ISeriesService, SeriesService>();

            return services;
        }
    }
}
