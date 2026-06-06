using System.Globalization;
using System.Xml.Linq;

namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Parsed subset of a Calibre <c>metadata.opf</c> sidecar. Fields are null/empty when the OPF
/// did not contain them, which lets the scanner fall back to whatever the ebook file itself
/// yielded (e.g. page count, embedded cover).
/// </summary>
public sealed record CalibreOpfMetadata
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Language { get; init; }

    public string? Publisher { get; init; }

    public string? Isbn { get; init; }

    public DateTime? PublishedOn { get; init; }

    public IReadOnlyList<string> AuthorNames { get; init; } = [];

    /// <summary>Calibre stores user "tags" as <c>&lt;dc:subject&gt;</c> entries.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? SeriesName { get; init; }

    public decimal? NumberInSeries { get; init; }

    public EbookCoverImage? Cover { get; init; }
}

public interface ICalibreOpfReader
{
    /// <summary>The OPF sidecar file name Calibre writes next to each book.</summary>
    const string OpfFileName = "metadata.opf";

    /// <summary>Returns the sibling <c>metadata.opf</c> path for a book file, or null when absent.</summary>
    string? FindOpfPath(string bookFilePath);

    /// <summary>
    /// Reads and parses the sibling <c>metadata.opf</c> for the given book file. Returns null when
    /// there is no sidecar or it could not be parsed. Never throws for malformed input.
    /// </summary>
    Task<CalibreOpfMetadata?> TryReadAsync(string bookFilePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads Calibre <c>metadata.opf</c> sidecars. Matches elements by local name (ignoring the OPF /
/// Dublin Core namespace prefixes) so it copes with the minor namespace variations different
/// Calibre versions emit.
/// </summary>
public sealed class CalibreOpfReader(ILogger<CalibreOpfReader> logger) : ICalibreOpfReader
{
    public string? FindOpfPath(string bookFilePath)
    {
        string? dir = Path.GetDirectoryName(bookFilePath);
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }

        string candidate = Path.Combine(dir, ICalibreOpfReader.OpfFileName);
        return File.Exists(candidate) ? candidate : null;
    }

    public async Task<CalibreOpfMetadata?> TryReadAsync(string bookFilePath, CancellationToken cancellationToken = default)
    {
        string? opfPath = FindOpfPath(bookFilePath);
        if (opfPath is null)
        {
            return null;
        }

        try
        {
            string xml = await File.ReadAllTextAsync(opfPath, cancellationToken);
            var doc = XDocument.Parse(xml);

            var metadata = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata");
            if (metadata is null)
            {
                return null;
            }

            string? title = FirstNonEmpty(metadata, "title");
            string? description = FirstNonEmpty(metadata, "description");
            string? language = FirstNonEmpty(metadata, "language");
            string? publisher = FirstNonEmpty(metadata, "publisher");

            var authors = metadata.Elements()
                .Where(e => e.Name.LocalName == "creator")
                .Where(IsAuthorRole)
                .Select(e => e.Value.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tags = metadata.Elements()
                .Where(e => e.Name.LocalName == "subject")
                .Select(e => e.Value.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string? isbn = ExtractIsbn(metadata);
            DateTime? publishedOn = ExtractPublishedOn(metadata);
            var (seriesName, numberInSeries) = ExtractSeries(metadata);
            var cover = await TryLoadCoverAsync(doc, opfPath, cancellationToken);

            return new CalibreOpfMetadata
            {
                Title = title,
                Description = description,
                Language = language,
                Publisher = publisher,
                Isbn = isbn,
                PublishedOn = publishedOn,
                AuthorNames = authors,
                Tags = tags,
                SeriesName = seriesName,
                NumberInSeries = numberInSeries,
                Cover = cover,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to parse Calibre metadata.opf at {Path}", opfPath);
            return null;
        }
    }

    private static bool IsAuthorRole(XElement creator)
    {
        // Calibre marks authors with opf:role="aut". When no role is present at all, treat the
        // creator as an author (some exports omit the attribute).
        var role = creator.Attributes().FirstOrDefault(a => a.Name.LocalName == "role");
        return role is null || string.Equals(role.Value, "aut", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(XElement metadata, string localName) => metadata.Elements()
        .Where(e => e.Name.LocalName == localName)
        .Select(e => e.Value.Trim())
        .FirstOrDefault(s => s.Length > 0);

    private static string? ExtractIsbn(XElement metadata)
    {
        foreach (var id in metadata.Elements().Where(e => e.Name.LocalName == "identifier"))
        {
            string value = id.Value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            var scheme = id.Attributes().FirstOrDefault(a => a.Name.LocalName == "scheme");
            if (scheme is not null && scheme.Value.Contains("isbn", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            if (value.Contains("isbn", StringComparison.OrdinalIgnoreCase))
            {
                int colon = value.LastIndexOf(':');
                return colon >= 0 ? value[(colon + 1)..].Trim() : value;
            }
        }

        return null;
    }

    private static DateTime? ExtractPublishedOn(XElement metadata)
    {
        foreach (var date in metadata.Elements().Where(e => e.Name.LocalName == "date"))
        {
            if (DateTime.TryParse(
                date.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var d))
            {
                return d;
            }
        }

        return null;
    }

    private static (string? Series, decimal? Number) ExtractSeries(XElement metadata)
    {
        string? series = null;
        string? index = null;

        foreach (var meta in metadata.Elements().Where(e => e.Name.LocalName == "meta"))
        {
            string? name = meta.Attributes().FirstOrDefault(a => a.Name.LocalName == "name")?.Value;
            string? content = meta.Attributes().FirstOrDefault(a => a.Name.LocalName == "content")?.Value;
            if (string.IsNullOrWhiteSpace(name) || content is null)
            {
                continue;
            }

            if (string.Equals(name, "calibre:series", StringComparison.OrdinalIgnoreCase))
            {
                series = content.Trim();
            }
            else if (string.Equals(name, "calibre:series_index", StringComparison.OrdinalIgnoreCase))
            {
                index = content.Trim();
            }
        }

        decimal? number = decimal.TryParse(index, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal n)
            ? n
            : null;

        return (string.IsNullOrWhiteSpace(series) ? null : series, number);
    }

    private async Task<EbookCoverImage?> TryLoadCoverAsync(XDocument doc, string opfPath, CancellationToken cancellationToken)
    {
        string opfDir = Path.GetDirectoryName(opfPath) ?? string.Empty;

        // Prefer the explicit <guide><reference type="cover" href="..."/></guide> pointer, then
        // fall back to Calibre's conventional cover.jpg sitting next to the OPF.
        string? href = doc.Descendants()
            .Where(e => e.Name.LocalName == "reference")
            .Where(e => string.Equals(
                e.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value,
                "cover",
                StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value)
            .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(href))
        {
            candidates.Add(Path.Combine(opfDir, href.Replace('/', Path.DirectorySeparatorChar)));
        }

        candidates.Add(Path.Combine(opfDir, "cover.jpg"));

        foreach (string path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                if (bytes.Length > 0)
                {
                    string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                    return new EbookCoverImage(bytes, ext.Length > 0 ? ext : "jpg");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not read Calibre cover candidate {Path}", path);
            }
        }

        return null;
    }
}
