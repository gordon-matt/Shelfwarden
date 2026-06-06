using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shelfwarden.Models.Metadata;

namespace Shelfwarden.Services.Metadata;

/// <summary>
/// Metadata source backed by the official Google Books API v1
/// (<c>https://www.googleapis.com/books/v1/volumes</c>). No API key is required for the modest
/// query volume this app generates. Calibre uses the legacy Atom feeds instead; the JSON REST API
/// is cleaner and is what Booklore's <c>GoogleParser</c> uses.
/// </summary>
public sealed class GoogleBooksMetadataProvider(
    ILogger<GoogleBooksMetadataProvider> logger,
    IHttpClientFactory httpClientFactory) : IBookMetadataProvider
{
    private const string BaseUrl = "https://www.googleapis.com/books/v1/volumes";

    public string Name => "Google Books";

    public int Priority => 0;

    public async Task<IReadOnlyList<ExternalBookMetadataDto>> SearchAsync(BookMetadataQuery query, CancellationToken cancellationToken = default)
    {
        string? q = BuildQuery(query);
        if (q is null)
        {
            return [];
        }

        int maxResults = Math.Clamp(query.Limit, 1, 40);
        string url = $"{BaseUrl}?q={Uri.EscapeDataString(q)}&maxResults={maxResults}&printType=books";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", MetadataHttp.UserAgent);

            var client = httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Google Books search returned {Status} for query '{Query}'", (int)response.StatusCode, q);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<GoogleVolumesResponse>(stream, JsonOptions, cancellationToken);
            if (payload?.Items is null || payload.Items.Count == 0)
            {
                return [];
            }

            var results = new List<ExternalBookMetadataDto>(payload.Items.Count);
            foreach (var item in payload.Items)
            {
                var dto = ToDto(item);
                if (dto is not null)
                {
                    results.Add(dto);
                }
            }

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Google Books search failed for query '{Query}'", q);
            return [];
        }
    }

    private static string? BuildQuery(BookMetadataQuery query)
    {
        if (query.HasIsbn)
        {
            string isbn = MetadataNormalization.NormalizeIsbn(query.Isbn!);
            if (isbn.Length > 0)
            {
                return $"isbn:{isbn}";
            }
        }

        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(query.Title))
        {
            parts.Add($"intitle:{query.Title.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            parts.Add($"inauthor:{query.Author.Trim()}");
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private ExternalBookMetadataDto? ToDto(GoogleVolume item)
    {
        var info = item.VolumeInfo;
        if (info is null || string.IsNullOrWhiteSpace(info.Title))
        {
            return null;
        }

        string? isbn = null;
        if (info.IndustryIdentifiers is { Count: > 0 })
        {
            isbn = info.IndustryIdentifiers.FirstOrDefault(i => i.Type == "ISBN_13")?.Identifier
                ?? info.IndustryIdentifiers.FirstOrDefault(i => i.Type == "ISBN_10")?.Identifier;
        }

        // Google "categories" come as e.g. "Fiction / Fantasy / Epic"; flatten to distinct segments.
        var genres = (info.Categories ?? [])
            .SelectMany(c => c.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ExternalBookMetadataDto(
            Provider: Name,
            ProviderId: item.Id,
            Title: info.Title.Trim(),
            Subtitle: MetadataNormalization.NullIfBlank(info.Subtitle),
            Description: MetadataNormalization.StripHtml(info.Description),
            Language: MetadataNormalization.NullIfBlank(info.Language)?.ToLowerInvariant(),
            Publisher: MetadataNormalization.NullIfBlank(info.Publisher),
            Isbn: MetadataNormalization.NullIfBlank(isbn),
            PublishedOn: MetadataNormalization.ParseDate(info.PublishedDate),
            PageCount: info.PageCount is > 0 ? info.PageCount : null,
            SeriesName: null,
            NumberInSeries: null,
            Authors: info.Authors ?? [],
            Genres: genres,
            Tags: [],
            InfoUrl: info.CanonicalVolumeLink ?? info.InfoLink);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class GoogleVolumesResponse
    {
        public List<GoogleVolume>? Items { get; set; }
    }

    private sealed class GoogleVolume
    {
        public string? Id { get; set; }

        public GoogleVolumeInfo? VolumeInfo { get; set; }
    }

    private sealed class GoogleVolumeInfo
    {
        public string? Title { get; set; }

        public string? Subtitle { get; set; }

        public List<string>? Authors { get; set; }

        public string? Publisher { get; set; }

        public string? PublishedDate { get; set; }

        public string? Description { get; set; }

        public List<GoogleIndustryIdentifier>? IndustryIdentifiers { get; set; }

        public int? PageCount { get; set; }

        public List<string>? Categories { get; set; }

        public string? Language { get; set; }

        public string? InfoLink { get; set; }

        public string? CanonicalVolumeLink { get; set; }
    }

    private sealed class GoogleIndustryIdentifier
    {
        public string? Type { get; set; }

        public string? Identifier { get; set; }
    }
}
