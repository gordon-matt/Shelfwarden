using Microsoft.Extensions.DependencyInjection;
using Shelfwarden.Services.Auth;
using Shelfwarden.Services.Scanning;
using Shelfwarden.Services.Storage;
using Shelfwarden.Services.Tts;

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

            services.AddScoped<IShelfService, ShelfService>();
            services.AddScoped<IBookService, BookService>();
            services.AddScoped<IAuthorService, AuthorService>();
            services.AddScoped<ISeriesService, SeriesService>();
            services.AddScoped<IGenreService, GenreService>();
            services.AddScoped<IServerSettingsService, ServerSettingsService>();
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<IBookmarkService, BookmarkService>();
            services.AddScoped<ICollectionService, CollectionService>();
            services.AddScoped<IReadingListService, ReadingListService>();
            services.AddScoped<ISetupService, SetupService>();

            // Storage + scanner. The metadata extractors are stateless so they can be singletons;
            // ScannerService itself is scoped because it pulls in EF Core repositories.
            services.AddSingleton<IStoragePathProvider, StoragePathProvider>();
            services.AddSingleton<IEbookMetadataExtractor, EpubMetadataExtractor>();
            services.AddSingleton<IEbookMetadataExtractor, PdfMetadataExtractor>();
            services.AddSingleton<IEbookMetadataExtractorFactory, EbookMetadataExtractorFactory>();
            services.AddSingleton<IScanProgressTracker, ScanProgressTracker>();
            services.AddScoped<IScannerService, ScannerService>();
            services.AddScoped<IScanStatusService, ScanStatusService>();

            // Text-to-speech. The Kokoro engine and FFmpeg binaries are big; both providers
            // are singletons and lazy-initialise themselves on first job. Per-format text
            // extractors and the chunker / stitcher are stateless helpers — also singletons
            // for the same reason as the metadata extractors above. The Hangfire entrypoint
            // (TtsJobService) is scoped because it speaks to EF Core via repositories.
            services.AddSingleton<IKokoroEngineProvider, KokoroEngineProvider>();
            services.AddSingleton<IFFmpegProvider, FFmpegProvider>();
            services.AddSingleton<IBookTextExtractor, EpubTextExtractor>();
            services.AddSingleton<IBookTextExtractor, PdfTextExtractor>();
            services.AddSingleton<IBookTextExtractorFactory, BookTextExtractorFactory>();
            services.AddSingleton<IAudiobookProgressTracker, AudiobookProgressTracker>();
            services.AddSingleton<AudioStitcher>();
            services.AddScoped<ITtsJobService, TtsJobService>();
            services.AddScoped<IAudiobookService, AudiobookService>();

            return services;
        }
    }
}