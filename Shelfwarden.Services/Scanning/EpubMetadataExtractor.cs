using System.Globalization;
using SixLabors.ImageSharp;
using VersOne.Epub;
using VersOne.Epub.Options;
using VersOne.Epub.Schema;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Extracts metadata + cover image from an EPUB file using <c>VersOne.Epub</c>. Configured
/// permissively (matching Kavita's "lenient" reader options) so partially malformed books
/// still produce useful output instead of aborting the scan.
/// </summary>
public sealed class EpubMetadataExtractor(ILogger<EpubMetadataExtractor> logger) : IEbookMetadataExtractor
{
    private static readonly EpubReaderOptions ReaderOptions = new()
    {
        PackageReaderOptions = new PackageReaderOptions
        {
            IgnoreMissingToc = true,
            SkipInvalidManifestItems = true,
        },
        XmlReaderOptions = new XmlReaderOptions { SkipXmlHeaders = true },
    };

    public EbookFormat Format => EbookFormat.Epub;

    public async Task<EbookMetadata> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var book = await EpubReader.ReadBookAsync(filePath, ReaderOptions);
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = book.Schema.Package.Metadata;

            string title = NullIfWhitespace(book.Title)
                ?? metadata.Titles.Select(t => t.Title).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                ?? Path.GetFileNameWithoutExtension(filePath);

            var (seriesName, numberInSeries, sortTitle) = ExtractSeriesInfo(metadata);

            var authorNames = book.AuthorList?
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            string? language = metadata.Languages.Select(l => l.Language).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            string? publisher = metadata.Publishers.Select(p => p.Publisher).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            string? description = NullIfWhitespace(book.Description)
                ?? metadata.Descriptions.Select(d => d.Description).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            string? isbn = ExtractIsbn(metadata);
            DateTime? publishedOn = ExtractPublishedOn(metadata);

            var genres = metadata.Subjects
                .Select(s => s.Subject)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            EbookCoverImage? cover = null;
            if (book.CoverImage is { Length: > 0 } coverBytes)
            {
                cover = new EbookCoverImage(coverBytes, GuessImageExtension(coverBytes));
            }

            int? pageCount = book.ReadingOrder?.Count;

            return new EbookMetadata
            {
                Title = title.Trim(),
                Subtitle = sortTitle is null ? null : NullIfWhitespace(sortTitle),
                Description = description,
                Language = language,
                Publisher = publisher,
                Isbn = isbn,
                PublishedOn = publishedOn,
                PageCount = pageCount,
                AuthorNames = authorNames,
                SeriesName = seriesName,
                NumberInSeries = numberInSeries,
                Genres = genres,
                Cover = cover,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to parse EPUB {FilePath}; falling back to filename-only metadata", filePath);
            return new EbookMetadata { Title = Path.GetFileNameWithoutExtension(filePath) };
        }
    }

    private static (string? Series, decimal? Number, string? SortTitle) ExtractSeriesInfo(EpubMetadata metadata)
    {
        string? series = null;
        string? number = null;
        string? sortTitle = null;

        foreach (var item in metadata.MetaItems ?? [])
        {
            switch (item.Name)
            {
                case "calibre:series": series = item.Content; break;
                case "calibre:series_index": number = item.Content; break;
                case "calibre:title_sort": sortTitle = item.Content; break;
            }

            switch (item.Property)
            {
                case "belongs-to-collection": series = item.Content; break;
                case "group-position": number = item.Content; break;
            }
        }

        decimal? parsedNumber = decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal n)
            ? n
            : null;

        return (NullIfWhitespace(series), parsedNumber, sortTitle);
    }

    private static string? ExtractIsbn(EpubMetadata metadata)
    {
        foreach (var id in metadata.Identifiers ?? [])
        {
            string? value = id.Identifier;
            if (string.IsNullOrWhiteSpace(value)) continue;
            // Common formats: "urn:isbn:9781234567890", "isbn:..." or "ISBN 978..."
            string lower = value.ToLowerInvariant();
            if (lower.Contains("isbn"))
            {
                int colon = lower.LastIndexOf(':');
                return value[(colon + 1)..].Trim();
            }
        }
        return null;
    }

    private static DateTime? ExtractPublishedOn(EpubMetadata metadata)
    {
        foreach (var date in metadata.Dates ?? [])
        {
            if (DateTime.TryParse(date.Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
            {
                return d;
            }
        }
        return null;
    }

    private static string GuessImageExtension(byte[] bytes)
    {
        try
        {
            var info = Image.Identify(bytes);
            return info?.Metadata.DecodedImageFormat?.FileExtensions.FirstOrDefault()?.ToLowerInvariant() ?? "jpg";
        }
        catch
        {
            return "jpg";
        }
    }

    private static string? NullIfWhitespace(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
