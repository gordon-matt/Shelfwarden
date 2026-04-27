using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shelfwarden.Services.Scanning;
using Shelfwarden.Services.Storage;

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
            services.AddScoped<IGenreService, GenreService>();
            services.AddScoped<IServerSettingsService, ServerSettingsService>();
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<IBookmarkService, BookmarkService>();
            services.AddScoped<ICollectionService, CollectionService>();
            services.AddScoped<IReadingListService, ReadingListService>();

            // Storage + scanner. The metadata extractors are stateless so they can be singletons;
            // ScannerService itself is scoped because it pulls in EF Core repositories.
            services.AddSingleton<IStoragePathProvider, StoragePathProvider>();
            services.AddSingleton<IEbookMetadataExtractor, EpubMetadataExtractor>();
            services.AddSingleton<IEbookMetadataExtractor, PdfMetadataExtractor>();
            services.AddSingleton<IEbookMetadataExtractorFactory, EbookMetadataExtractorFactory>();
            services.AddScoped<IScannerService, ScannerService>();
            services.AddScoped<IScanStatusService, ScanStatusService>();

            return services;
        }
    }
}
