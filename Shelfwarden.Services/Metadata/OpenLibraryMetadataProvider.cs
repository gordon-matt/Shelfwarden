using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shelfwarden.Models.Metadata;

namespace Shelfwarden.Services.Metadata;

/// <summary>
/// Metadata source backed by the Open Library public JSON API
/// (<c>https://openlibrary.org/search.json</c> + per-work hydration). Calibre only uses Open Library
/// for cover art, but its search/works endpoints expose full bibliographic metadata, and the app
/// already depends on Open Library for author imports.
/// </summary>
public sealed class OpenLibraryMetadataProvider(
    ILogger<OpenLibraryMetadataProvider> logger,
    IHttpClientFactory httpClientFactory) : IBookMetadataProvider
{
    private const string SearchUrl = "https://openlibrary.org/search.json";
    private const string BaseUrl = "https://openlibrary.org";
    private const int MaxSubjectsAsTags = 10;

    public string Name => "Open Library";

    public int Priority => 1;

    public async Task<IReadOnlyList<ExternalBookMetadataDto>> SearchAsync(BookMetadataQuery query, CancellationToken cancellationToken = default)
    {
        string? url = BuildSearchUrl(query);
        if (url is null)
        {
            return [];
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            var payload = await GetJsonAsync<OpenLibrarySearchResponse>(client, url, cancellationToken);
            if (payload?.Docs is null || payload.Docs.Count == 0)
            {
                return [];
            }

            // Hydrate each candidate's description from its work record in parallel (search docs
            // don't carry descriptions). Mirrors how AuthorService hydrates author detail records.
            var tasks = payload.Docs
                .Where(d => !string.IsNullOrWhiteSpace(d.Title))
                .Select(doc => ToDtoAsync(client, doc, cancellationToken));

            var results = await Task.WhenAll(tasks);
            return results.Where(r => r is not null).Select(r => r!).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Open Library search failed for url '{Url}'", url);
            return [];
        }
    }

    private static string? BuildSearchUrl(BookMetadataQuery query)
    {
        int limit = Math.Clamp(query.Limit, 1, 20);
        const string fields = "key,title,subtitle,author_name,first_publish_year,publisher,isbn,number_of_pages_median,language,subject";

        if (query.HasIsbn)
        {
            string isbn = MetadataNormalization.NormalizeIsbn(query.Isbn!);
            if (isbn.Length > 0)
            {
                return $"{SearchUrl}?isbn={Uri.EscapeDataString(isbn)}&fields={fields}&limit={limit}";
            }
        }

        var parts = new List<string> { $"fields={fields}", $"limit={limit}" };
        if (!string.IsNullOrWhiteSpace(query.Title))
        {
            parts.Add($"title={Uri.EscapeDataString(query.Title.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            parts.Add($"author={Uri.EscapeDataString(query.Author.Trim())}");
        }

        bool hasSearchTerm = parts.Any(p => p.StartsWith("title=", StringComparison.Ordinal) || p.StartsWith("author=", StringComparison.Ordinal));
        return hasSearchTerm ? $"{SearchUrl}?{string.Join('&', parts)}" : null;
    }

    private async Task<ExternalBookMetadataDto?> ToDtoAsync(HttpClient client, OpenLibraryDoc doc, CancellationToken cancellationToken)
    {
        string? description = await TryGetWorkDescriptionAsync(client, doc.Key, cancellationToken);

        string? isbn = doc.Isbn?.FirstOrDefault(i => i is { Length: 13 })
            ?? doc.Isbn?.FirstOrDefault();

        var tags = (doc.Subject ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSubjectsAsTags)
            .ToList();

        DateTime? publishedOn = doc.FirstPublishYear is > 0
            ? new DateTime(doc.FirstPublishYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            : null;

        string? infoUrl = string.IsNullOrWhiteSpace(doc.Key) ? null : $"{BaseUrl}{doc.Key}";

        return new ExternalBookMetadataDto(
            Provider: Name,
            ProviderId: doc.Key,
            Title: doc.Title!.Trim(),
            Subtitle: MetadataNormalization.NullIfBlank(doc.Subtitle),
            Description: description,
            // A work's `language` aggregates every edition's language in arbitrary order, so a
            // multi-language array can't tell us the book's actual language — only trust it when
            // there's a single, unambiguous entry (Google Books fills the gap otherwise).
            Language: doc.Language is { Count: 1 } ? MapLanguage(doc.Language[0]) : null,
            Publisher: MetadataNormalization.NullIfBlank(doc.Publisher?.FirstOrDefault()),
            Isbn: MetadataNormalization.NullIfBlank(isbn),
            PublishedOn: publishedOn,
            PageCount: doc.NumberOfPagesMedian is > 0 ? doc.NumberOfPagesMedian : null,
            SeriesName: null,
            NumberInSeries: null,
            Authors: doc.AuthorName ?? [],
            Genres: [],
            Tags: tags,
            InfoUrl: infoUrl);
    }

    private async Task<string?> TryGetWorkDescriptionAsync(HttpClient client, string? workKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workKey) || !workKey.StartsWith("/works/", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var work = await GetJsonAsync<OpenLibraryWork>(client, $"{BaseUrl}{workKey}.json", cancellationToken);
            // Open Library descriptions are sometimes a bare string, sometimes { "value": "..." }.
            string? raw = work?.Description?.Text;
            return MetadataNormalization.StripHtml(raw);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not hydrate Open Library work description for {Key}", workKey);
            return null;
        }
    }

    private async Task<T?> GetJsonAsync<T>(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", MetadataHttp.UserAgent);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Open Library request to {Url} returned {Status}", url, (int)response.StatusCode);
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    /// <summary>Maps the MARC 3-letter language codes Open Library returns to ISO-639-1 where we can.</summary>
    private static string? MapLanguage(string? marc)
    {
        if (string.IsNullOrWhiteSpace(marc))
        {
            return null;
        }

        string value = marc.Trim().ToLowerInvariant();
        return value switch
        {
            "eng" => "en",
            "fre" or "fra" => "fr",
            "ger" or "deu" => "de",
            "spa" => "es",
            "ita" => "it",
            "por" => "pt",
            "rus" => "ru",
            "jpn" => "ja",
            "chi" or "zho" => "zh",
            "dut" or "nld" => "nl",
            "swe" => "sv",
            "pol" => "pl",
            _ => value.Length <= 3 ? value : value[..2],
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class OpenLibrarySearchResponse
    {
        public List<OpenLibraryDoc>? Docs { get; set; }
    }

    private sealed class OpenLibraryDoc
    {
        public string? Key { get; set; }

        public string? Title { get; set; }

        public string? Subtitle { get; set; }

        [JsonPropertyName("author_name")]
        public List<string>? AuthorName { get; set; }

        [JsonPropertyName("first_publish_year")]
        public int? FirstPublishYear { get; set; }

        public List<string>? Publisher { get; set; }

        public List<string>? Isbn { get; set; }

        [JsonPropertyName("number_of_pages_median")]
        public int? NumberOfPagesMedian { get; set; }

        public List<string>? Language { get; set; }

        public List<string>? Subject { get; set; }
    }

    private sealed class OpenLibraryWork
    {
        [JsonConverter(typeof(OpenLibraryDescriptionConverter))]
        public OpenLibraryDescription? Description { get; set; }
    }

    private sealed class OpenLibraryDescription
    {
        public string? Text { get; set; }
    }

    /// <summary>Handles Open Library's polymorphic <c>description</c> (string OR <c>{ "value": "..." }</c>).</summary>
    private sealed class OpenLibraryDescriptionConverter : JsonConverter<OpenLibraryDescription>
    {
        public override OpenLibraryDescription? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return new OpenLibraryDescription { Text = reader.GetString() };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return doc.RootElement.TryGetProperty("value", out var value)
                    ? new OpenLibraryDescription { Text = value.GetString() }
                    : new OpenLibraryDescription();
            }

            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, OpenLibraryDescription value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Text);
    }
}
