using System.Globalization;
using System.IO.Compression;
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
    private static readonly EpubReaderOptions ReaderOptions = CreateReaderOptions();

    private static EpubReaderOptions CreateReaderOptions() => new(EpubReaderOptionsPreset.RELAXED)
    {
        XmlReaderOptions = new XmlReaderOptions(EpubReaderOptionsPreset.RELAXED) { SkipXmlHeaders = true },
        PackageReaderOptions =
        {
            IgnoreMissingToc = true,
            SkipInvalidManifestItems = true,
            // Some publisher OPF files still declare version="1.0"; treat as EPUB 2 for parsing.
            FallbackEpubVersion = EpubVersion.EPUB_2,
        },
        BookCoverReaderOptions =
        {
            // Malformed <meta name="cover"> (wrong manifest id or path) must not abort the whole read.
            Epub2MetadataIgnoreMissingManifestItem = true,
            Epub2MetadataIgnoreMissingContentFile = true,
            // cover-image items VersOne cannot load (e.g. BMP) must not abort the whole read.
            Epub3IgnoreMissingContentFile = true,
        },
        // NCX often references spine paths removed during conversion (e.g. calibre); VersOne throws
        // "content source ... not found in EPUB manifest" unless we skip those navigation entries.
        NavigationReaderOptions = new NavigationReaderOptions(EpubReaderOptionsPreset.RELAXED),
        SpineReaderOptions = { IgnoreMissingManifestItems = true },
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
                cover = TryExtractCoverFromManifestHints(book)
                    ?? TryExtractCoverFromXhtmlFallback(book);
            }

            int? pageCount = book.ReadingOrder?.Count;

            return new EbookMetadata
            {
                Title = title.Trim(),
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

    private static string GuessImageExtension(byte[] bytes, string? hrefHint = null)
    {
        try
        {
            var info = Image.Identify(bytes);
            string? ext = info?.Metadata.DecodedImageFormat?.FileExtensions.FirstOrDefault()?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(ext))
            {
                return ext;
            }
        }
        catch
        {
            // Fall back to manifest / zip entry extension below.
        }

        if (!string.IsNullOrWhiteSpace(hrefHint))
        {
            string fromPath = Path.GetExtension(hrefHint).TrimStart('.').ToLowerInvariant();
            if (fromPath.Length > 0)
            {
                return fromPath;
            }
        }

        return "jpg";
    }

    private static EbookCoverImage? TryExtractCoverFromManifestHints(EpubBook book)
    {
        foreach (string imageKey in EnumerateCoverManifestImageKeys(book))
        {
            EbookCoverImage? cover = TryLoadCoverImageBytes(book, imageKey);
            if (cover is not null)
            {
                return cover;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCoverManifestImageKeys(EpubBook book)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifest = book.Schema.Package.Manifest;
        if (manifest?.Items is not { Count: > 0 } items)
        {
            yield break;
        }

        foreach (var item in items)
        {
            if (item.Properties?.Contains(EpubManifestProperty.COVER_IMAGE) != true)
            {
                continue;
            }

            string key = StripFragmentAndNormalize(item.Href);
            if (key.Length > 0 && seen.Add(key))
            {
                yield return key;
            }
        }

        string? metaCoverRef = book.Schema.Package.Metadata.MetaItems?
            .FirstOrDefault(m => string.Equals(m.Name, "cover", StringComparison.OrdinalIgnoreCase))
            ?.Content;
        if (string.IsNullOrWhiteSpace(metaCoverRef))
        {
            yield break;
        }

        metaCoverRef = metaCoverRef.Trim();
        EpubManifestItem? manifestItem = items.FirstOrDefault(i => string.Equals(i.Id, metaCoverRef, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i => string.Equals(i.Href, metaCoverRef, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i => metaCoverRef.EndsWith('/' + i.Href, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(i => metaCoverRef.EndsWith(i.Href, StringComparison.OrdinalIgnoreCase));

        if (manifestItem?.Href is not { Length: > 0 } href)
        {
            yield break;
        }

        string metaKey = StripFragmentAndNormalize(href);
        if (metaKey.Length > 0 && seen.Add(metaKey))
        {
            yield return metaKey;
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

            EbookCoverImage? cover = TryLoadCoverImageBytes(book, imageKey, htmlKey);
            if (cover is not null)
            {
                return cover;
            }
        }

        return null;
    }

    private static EbookCoverImage? TryLoadCoverImageBytes(EpubBook book, string imageKey, string? htmlManifestKey = null)
    {
        if (TryGetImageFileByKey(book, imageKey) is { Content: { Length: > 0 } bytes })
        {
            return new EbookCoverImage(bytes, GuessImageExtension(bytes, imageKey));
        }

        if (TryReadImageBytesFromEpubZip(book, imageKey, htmlManifestKey) is { Length: > 0 } zipBytes)
        {
            return new EbookCoverImage(zipBytes, GuessImageExtension(zipBytes, imageKey));
        }

        return null;
    }

    private static byte[]? TryReadImageBytesFromEpubZip(EpubBook book, string imageManifestKey, string? htmlManifestKey = null)
    {
        string? epubPath = book.FilePath;
        if (string.IsNullOrEmpty(epubPath) || !File.Exists(epubPath))
        {
            return null;
        }

        string? contentRoot = GetEpubContentRootPrefix(book, htmlManifestKey);
        if (contentRoot is null)
        {
            return null;
        }

        string zipEntryPath = contentRoot + imageManifestKey.Replace('\\', '/');
        try
        {
            using var archive = ZipFile.OpenRead(epubPath);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Replace('\\', '/').Equals(zipEntryPath, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return null;
            }

            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetEpubContentRootPrefix(EpubBook book, string? referenceManifestKey)
    {
        if (!string.IsNullOrEmpty(referenceManifestKey)
            && TryGetHtmlFileByKey(book, referenceManifestKey, out var html)
            && html is not null)
        {
            string? prefix = GetPrefixFromFilePathAndKey(html.FilePath, html.Key);
            if (prefix is not null)
            {
                return prefix;
            }
        }

        foreach (var localHtml in book.Content.Html.Local)
        {
            string? prefix = GetPrefixFromFilePathAndKey(localHtml.FilePath, localHtml.Key);
            if (prefix is not null)
            {
                return prefix;
            }
        }

        foreach (var localImage in book.Content.Images.Local)
        {
            string? prefix = GetPrefixFromFilePathAndKey(localImage.FilePath, localImage.Key);
            if (prefix is not null)
            {
                return prefix;
            }
        }

        return null;
    }

    private static string? GetPrefixFromFilePathAndKey(string? filePath, string? manifestKey)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(manifestKey))
        {
            return null;
        }

        return filePath.EndsWith(manifestKey, StringComparison.OrdinalIgnoreCase)
            ? filePath[..^manifestKey.Length]
            : null;
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