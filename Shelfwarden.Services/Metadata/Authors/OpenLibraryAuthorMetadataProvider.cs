using System.Net.Http;
using System.Text.Json;
using OpenLibraryNET.Loader;

namespace Shelfwarden.Services.Metadata.Authors;

/// <summary>
/// Author metadata from Open Library. Searches <c>/search/authors.json</c>, then hydrates each
/// candidate from <c>/authors/{id}.json</c> for a real bio + photo, and pulls the author's most
/// widely-published works (by edition count) for the "notable books" preview.
/// </summary>
public sealed class OpenLibraryAuthorMetadataProvider(
    ILogger<OpenLibraryAuthorMetadataProvider> logger,
    IHttpClientFactory httpClientFactory) : IAuthorMetadataProvider
{
    public string Name => "OpenLibrary";

    public int Priority => 0;

    public async Task<IReadOnlyList<ExternalAuthorMatchDto>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        int clampedLimit = Math.Clamp(limit, 1, 20);
        string trimmed = query.Trim();

        try
        {
            var client = httpClientFactory.CreateClient();
            var rows = await OLSearchLoader.GetAuthorSearchResultsAsync(
                client,
                trimmed,
                new KeyValuePair<string, string>("limit", clampedLimit.ToString()));

            var baseMatches = (rows ?? [])
                .Select(a => new { Id = NormalizeOpenLibraryAuthorId(a.ID), a.Name })
                .Where(a => !string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(a.Name))
                .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var matches = new List<ExternalAuthorMatchDto>(baseMatches.Count);
            foreach (var candidate in baseMatches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var detail = await OLAuthorLoader.GetDataAsync(client, candidate.Id);
                if (detail is null)
                {
                    continue;
                }

                bool hasBio = !string.IsNullOrWhiteSpace(detail.Bio);
                bool hasPhoto = detail.PhotosIDs.Count > 0;
                if (!hasBio && !hasPhoto)
                {
                    continue;
                }

                string? topBooks = await GetTopBooksAsync(client, candidate.Id);
                int photoId = detail.PhotosIDs.FirstOrDefault();
                string olid = NormalizeOpenLibraryAuthorId(detail.ID);

                matches.Add(new ExternalAuthorMatchDto(
                    Provider: Name,
                    ProviderId: olid,
                    Name: string.IsNullOrWhiteSpace(detail.Name) ? candidate.Name : detail.Name.Trim(),
                    BirthDate: NullIfBlank(detail.BirthDate),
                    DeathDate: NullIfBlank(detail.DeathDate),
                    Biography: NullIfBlank(detail.Bio),
                    PhotoUrl: photoId > 0 ? $"https://covers.openlibrary.org/a/id/{photoId}-L.jpg" : null,
                    TopBooks: topBooks,
                    InfoUrl: $"https://openlibrary.org/authors/{olid}"));
            }

            return matches;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "OpenLibrary author search failed for '{Query}'", trimmed);
            return [];
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeOpenLibraryAuthorId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string value = raw.Trim();
        if (value.StartsWith("/authors/", StringComparison.OrdinalIgnoreCase))
        {
            value = value["/authors/".Length..];
        }

        return value;
    }

    private async Task<string?> GetTopBooksAsync(HttpClient client, string olid, int count = 3)
    {
        try
        {
            var titles = await FetchTopBookTitlesAsync(client, olid, count, englishOnly: true);
            if (titles.Count < count)
            {
                titles = await FetchTopBookTitlesAsync(client, olid, count, englishOnly: false);
            }

            return titles.Count > 0 ? string.Join(", ", titles) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to fetch works for OpenLibrary author '{OLId}'", olid);
            return null;
        }
    }

    private static async Task<List<string>> FetchTopBookTitlesAsync(HttpClient client, string olid, int count, bool englishOnly)
    {
        int fetchLimit = count + 3;
        string langSegment = englishOnly ? "&language=eng" : string.Empty;
        string url = $"https://openlibrary.org/search.json?author_key={olid}&sort=editions&limit={fetchLimit}&fields=title{langSegment}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", MetadataHttp.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        if (!doc.RootElement.TryGetProperty("docs", out var docs))
        {
            return [];
        }

        return docs.EnumerateArray()
            .Select(d => d.TryGetProperty("title", out var t) ? t.GetString()?.Trim() : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Take(count)
            .ToList()!;
    }
}
