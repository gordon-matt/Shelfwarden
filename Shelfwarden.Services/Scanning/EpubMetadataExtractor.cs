using System.Globalization;
using System.Text.RegularExpressions;
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
public sealed partial class EpubMetadataExtractor(ILogger<EpubMetadataExtractor> logger) : IEbookMetadataExtractor
{
    private static readonly EpubReaderOptions ReaderOptions = new()
    {
        PackageReaderOptions = new PackageReaderOptions
        {
            IgnoreMissingToc = true,
            SkipInvalidManifestItems = true,
        },
        XmlReaderOptions = new XmlReaderOptions { SkipXmlHeaders = true },
        // NCX often references spine paths removed during conversion (e.g. calibre); VersOne throws
        // "content source ... not found in EPUB manifest" unless we skip those navigation entries.
        NavigationReaderOptions = new NavigationReaderOptions(EpubReaderOptionsPreset.RELAXED),
    };

    public EbookFormat Format => EbookFormat.Epub;

    public async Task<EbookMetadata> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var book = await EpubReader.ReadBookAsync(filePath, ReaderOptions);
            if (book is null)
            {
                return new EbookMetadata { Title = Path.GetFileNameWithoutExtension(filePath) };
            }

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
            var publishedOn = ExtractPublishedOn(metadata);

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
            else
            {
                // VersOne.Epub resolves EPUB2 guide covers by looking up the guide href in the
                // *image* map only. Many books use guide href to XHTML/SVG (cover flow) with the
                // bitmap referenced inside (img src, SVG image xlink:href). Those never populate
                // CoverImage; parse the wrapper page(s) and load the first local image.
                cover = TryExtractCoverFromXhtmlFallback(book);
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
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
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

    private static EbookCoverImage? TryExtractCoverFromXhtmlFallback(EpubBook book)
    {
        foreach (string htmlKey in EnumerateCoverHtmlManifestKeys(book))
        {
            if (!TryGetHtmlFileByKey(book, htmlKey, out var html) || html is null)
            {
                continue;
            }

            if (FindFirstLocalImageUrl(html.Content) is not { } url)
            {
                continue;
            }

            string imageKey = ResolveOpfRelativeHref(htmlKey, url);
            if (string.IsNullOrEmpty(imageKey))
            {
                continue;
            }

            if (TryGetImageFileByKey(book, imageKey) is { Content: { Length: > 0 } bytes })
            {
                return new EbookCoverImage(bytes, GuessImageExtension(bytes));
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCoverHtmlManifestKeys(EpubBook book)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (book.Schema.Package.Guide?.Items is { } guideItems)
        {
            foreach (var g in guideItems)
            {
                if (!string.Equals(g.Type, "cover", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string key = StripFragmentAndNormalize(g.Href);
                if (key.Length > 0 && seen.Add(key))
                {
                    yield return key;
                }
            }
        }

        if (book.ReadingOrder is not { } order)
        {
            yield break;
        }

        const int maxSpineItems = 3;
        for (int i = 0; i < order.Count && i < maxSpineItems; i++)
        {
            string key = order[i].Key;
            if (key.Length > 0 && seen.Add(key))
            {
                yield return key;
            }
        }
    }

    private static string StripFragmentAndNormalize(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        string t = href.Trim();
        int hash = t.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            t = t[..hash];
        }

        return t.Trim();
    }

    private static bool TryGetHtmlFileByKey(EpubBook book, string key, out EpubLocalTextContentFile? file)
    {
        if (book.Content.Html.TryGetLocalFileByKey(key, out file))
        {
            return true;
        }

        foreach (var h in book.Content.Html.Local)
        {
            if (h.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                file = h;
                return true;
            }
        }

        file = null;
        return false;
    }

    private static EpubLocalByteContentFile? TryGetImageFileByKey(EpubBook book, string key)
    {
        if (book.Content.Images.TryGetLocalFileByKey(key, out var img))
        {
            return img;
        }

        foreach (var b in book.Content.Images.Local)
        {
            if (b.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return b;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the href of the first packaged (non-remote) image referenced from XHTML/SVG cover markup.
    /// </summary>
    private static string? FindFirstLocalImageUrl(string xhtml)
    {
        var candidates = new List<(int Index, string Url)>();

        foreach (Match m in SvgImageHrefRegex().Matches(xhtml))
        {
            string u = m.Groups[1].Value.Trim();
            if (!IsPackagedRelativeReference(u))
            {
                continue;
            }

            candidates.Add((m.Index, u));
        }

        foreach (Match m in ImgSrcRegex().Matches(xhtml))
        {
            string u = m.Groups[1].Value.Trim();
            if (!IsPackagedRelativeReference(u))
            {
                continue;
            }

            candidates.Add((m.Index, u));
        }

        return candidates.Count == 0 ? null : candidates.MinBy(t => t.Index).Url;
    }

    private static bool IsPackagedRelativeReference(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        string t = href.Trim();
        return !t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !t.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Join <paramref name="documentKey"/> (manifest href of the current XHTML) with a relative image URL.
    /// </summary>
    private static string ResolveOpfRelativeHref(string documentKey, string relativeRef)
    {
        relativeRef = StripFragmentAndNormalize(relativeRef);
        if (relativeRef.Length == 0)
        {
            return string.Empty;
        }

        string baseDir = "";
        int slash = documentKey.LastIndexOf('/');
        if (slash >= 0)
        {
            baseDir = documentKey[..(slash + 1)];
        }

        string combined = baseDir + relativeRef;
        var stack = new List<string>();
        foreach (string seg in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".")
            {
                continue;
            }

            if (seg == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
            }
            else
            {
                stack.Add(seg);
            }
        }

        return string.Join("/", stack);
    }

    [GeneratedRegex(@"<image\b[^>]*?\s(?:xlink:href|href)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SvgImageHrefRegex();

    [GeneratedRegex(@"<img\b[^>]*?\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImgSrcRegex();

    private static string? NullIfWhitespace(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}