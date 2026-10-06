namespace Shelfwarden.Services.Opds;

/// <summary>
/// Builds every URL that appears in an OPDS document. Links are host-relative paths (no scheme or
/// host), so they're correct behind any reverse proxy without trusting forwarded headers, and can't
/// leak an internal hostname. Clients resolve them against the URL they requested.
/// </summary>
/// <param name="pathBase">The request's path base (e.g. <c>/shelfwarden</c>), or empty.</param>
/// <param name="pathKey">
/// The OPDS key when the client authenticated with a key in the URL, so links keep carrying it;
/// null for HTTP Basic clients.
/// </param>
public sealed class OpdsLinkBuilder(string? pathBase = null, string? pathKey = null)
{
    public const string RoutePrefix = "opds";

    /// <summary>Path segment that introduces a key-in-URL route: <c>/opds/key/{key}/…</c>.</summary>
    public const string KeyRouteSegment = "key";

    private readonly string pathBase = (pathBase ?? string.Empty).TrimEnd('/');

    /// <summary>Root of the catalogue, e.g. <c>/opds</c> or <c>/opds/key/abc123</c>.</summary>
    public string CatalogRoot => string.IsNullOrEmpty(pathKey)
        ? $"{pathBase}/{RoutePrefix}"
        : $"{pathBase}/{RoutePrefix}/{KeyRouteSegment}/{Uri.EscapeDataString(pathKey)}";

    public string Icon => $"{pathBase}/favicon.ico";

    public string Root() => CatalogRoot;

    public string Recent(int page = 1) => Paged("recent", page);

    public string AllBooks(int page = 1) => Paged("all", page);

    public string Authors(int page = 1) => Paged("authors", page);

    public string Author(int authorId, int page = 1) => Paged($"authors/{authorId}", page);

    public string SeriesList(int page = 1) => Paged("series", page);

    public string Series(int seriesId, int page = 1) => Paged($"series/{seriesId}", page);

    public string Shelves() => $"{CatalogRoot}/shelves";

    public string Shelf(int shelfId, int page = 1) => Paged($"shelves/{shelfId}", page);

    public string Search(string query, int page = 1)
    {
        string url = $"{CatalogRoot}/search?q={Uri.EscapeDataString(query)}";
        return page > 1 ? $"{url}&page={page}" : url;
    }

    /// <summary>OpenSearch URL template; clients substitute <c>{searchTerms}</c>.</summary>
    public string SearchTemplate() => $"{CatalogRoot}/search?q={{searchTerms}}";

    public string OpenSearchDescription() => $"{CatalogRoot}/opensearch.xml";

    /// <summary>
    /// Download URL. The trailing file name is ignored by the server; some clients name the saved
    /// file after the last URL segment instead of reading <c>Content-Disposition</c>.
    /// </summary>
    public string Download(int bookId, string fileName) =>
        $"{CatalogRoot}/books/{bookId}/download/{Uri.EscapeDataString(fileName)}";

    public string Cover(int bookId) => $"{CatalogRoot}/books/{bookId}/cover";

    public string Thumbnail(int bookId) => $"{CatalogRoot}/books/{bookId}/thumbnail";

    private string Paged(string relative, int page) =>
        page > 1 ? $"{CatalogRoot}/{relative}?page={page}" : $"{CatalogRoot}/{relative}";
}
