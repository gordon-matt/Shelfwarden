using Microsoft.Extensions.DependencyInjection;
using Shelfwarden.Services.Jobs;
using Shelfwarden.Services.Metadata;
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
            services.AddScoped<IUserContextService, BlazorAwareUserContextService>();
            services.AddScoped<IShelfAccessService, ShelfAccessService>();

            services.AddScoped<IShelfService, ShelfService>();
            services.AddScoped<IBookService, BookService>();
            services.AddScoped<IBookCoverService, BookCoverService>();
            services.AddScoped<IAuthorService, AuthorService>();
            services.AddScoped<ISeriesService, SeriesService>();
            services.AddScoped<IGenreService, GenreService>();
            services.AddScoped<ITagService, TagService>();
            services.AddScoped<IAdditionalContentTagService, AdditionalContentTagService>();
            services.AddScoped<IServerSettingsService, ServerSettingsService>();
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<IBookmarkService, BookmarkService>();
            services.AddScoped<ICollectionService, CollectionService>();
            services.AddScoped<IReadingListService, ReadingListService>();
            services.AddScoped<ISetupService, SetupService>();
            services.AddScoped<IAdditionalContentService, AdditionalContentService>();
            services.AddScoped<AuthorCleanupJob>();

            // Storage + scanner. The metadata extractors are stateless so they can be singletons;
            // ScannerService itself is scoped because it pulls in EF Core repositories.
            services.AddSingleton<IStoragePathProvider, StoragePathProvider>();
            services.AddSingleton<IEbookMetadataExtractor, EpubMetadataExtractor>();
            services.AddSingleton<IEbookMetadataExtractor, PdfMetadataExtractor>();
            services.AddSingleton<IEbookMetadataExtractorFactory, EbookMetadataExtractorFactory>();
            services.AddSingleton<ICalibreOpfReader, CalibreOpfReader>();
            services.AddSingleton<IScanProgressTracker, ScanProgressTracker>();
            services.AddScoped<IScannerService, ScannerService>();
            services.AddScoped<IScanStatusService, ScanStatusService>();

            // Online metadata sources (Google Books, Open Library, …). Providers are stateless
            // singletons that talk to public HTTP APIs via IHttpClientFactory; the aggregating
            // IBookMetadataService is scoped because its admin gate needs IUserContextService.
            services.AddSingleton<IBookMetadataProvider, GoogleBooksMetadataProvider>();
            services.AddSingleton<IBookMetadataProvider, OpenLibraryMetadataProvider>();
            // Amazon + Goodreads scrape public pages (no API); kept lower priority than the API
            // sources because scraped pages are more fragile and rate-limited.
            services.AddSingleton<IBookMetadataProvider, AmazonMetadataProvider>();
            services.AddSingleton<IBookMetadataProvider, GoodreadsMetadataProvider>();
            services.AddScoped<IBookMetadataService, BookMetadataService>();

            // Text-to-speech. The Kokoro engine and FFmpeg binaries are big; both providers
            // are singletons and lazy-initialise themselves on first job. Per-format text
            // extractors and the chunker / stitcher are stateless helpers — also singletons
            // for the same reason as the metadata extractors above. The Hangfire entrypoint
            // (TtsJobService) is scoped because it speaks to EF Core via repositories.
            services.AddSingleton<IKokoroEngineProvider, KokoroEngineProvider>();
            services.AddSingleton<IFFmpegProvider, FFmpegProvider>();
            services.AddSingleton<IEbookSectionParser, EpubSectionParser>();
            services.AddSingleton<IEbookSectionParser, PdfSectionParser>();
            services.AddSingleton<IEbookSectionParserFactory, EbookSectionParserFactory>();
            services.AddSingleton<IAudiobookProgressTracker, AudiobookProgressTracker>();
            services.AddSingleton<AudioStitcher>();
            services.AddScoped<ITtsJobService, TtsJobService>();
            services.AddScoped<IAudiobookService, AudiobookService>();

            return services;
        }
    }
}