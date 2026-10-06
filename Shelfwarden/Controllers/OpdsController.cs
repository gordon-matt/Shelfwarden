using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Shelfwarden.Infrastructure.Opds;
using Shelfwarden.Models.Opds;
using Shelfwarden.Services.Opds;
using Shelfwarden.Services.Storage;
using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;

namespace Shelfwarden.Controllers;

/// <summary>
/// OPDS 1.2 catalogue for e-reader apps. Every route is available both at <c>/opds/...</c>
/// (HTTP Basic: user name + OPDS key) and at <c>/opds/key/{apiKey}/...</c> for clients that can't
/// send credentials. Links inside feeds are host-relative and keep whichever form the client used.
/// Books on shelves the caller can't access answer 404, as if they didn't exist.
/// </summary>
[Authorize(AuthenticationSchemes = OpdsAuthenticationHandler.SchemeName)]
[Route("opds")]
[Route("opds/key/{" + OpdsAuthenticationHandler.ApiKeyRouteValue + "}")]
public class OpdsController(
    ILogger<OpdsController> logger,
    IOpdsFeedService feedService,
    ICoverThumbnailService thumbnailService,
    IShelfAccessService shelfAccessService,
    IStoragePathProvider storage,
    IOptions<OpdsOptions> options,
    IRepository<Book> bookRepository) : ControllerBase
{
    private static readonly TimeSpan CoverCacheDuration = TimeSpan.FromDays(1);

    /// <summary>Root navigation feed.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Root(CancellationToken cancellationToken) =>
        FeedResult(await feedService.GetRootAsync(Links, cancellationToken));

    /// <summary>Books, newest first.</summary>
    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        FeedResult(await feedService.GetRecentAsync(Links, page, cancellationToken));

    /// <summary>All books by title.</summary>
    [HttpGet("all")]
    public async Task<IActionResult> AllBooks([FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        FeedResult(await feedService.GetAllBooksAsync(Links, page, cancellationToken));

    /// <summary>Authors and pseudonyms with visible books.</summary>
    [HttpGet("authors")]
    public async Task<IActionResult> Authors([FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        FeedResult(await feedService.GetAuthorsAsync(Links, page, cancellationToken));

    /// <summary>Books by one author or pseudonym.</summary>
    [HttpGet("authors/{authorId:int}")]
    public async Task<IActionResult> Author(int authorId, [FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        FeedResult(await feedService.GetAuthorBooksAsync(Links, authorId, page, cancellationToken));

    /// <summary>Series with visible books.</summary>
    [HttpGet("series")]
    public async Task<IActionResult> SeriesList([FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        FeedResult(await feedService.GetSeriesListAsync(Links, page, cancellationToken));

    /// <summary>Books in one series, in reading order.</summary>
    [HttpGet("series/{seriesId:int}")]
    public async Task<IActionResult> Series(int seriesId, [FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        FeedResult(await feedService.GetSeriesBooksAsync(Links, seriesId, page, cancellationToken));

    /// <summary>Shelves the caller can access.</summary>
    [HttpGet("shelves")]
    public async Task<IActionResult> Shelves(CancellationToken cancellationToken) =>
        FeedResult(await feedService.GetShelvesAsync(Links, cancellationToken));

    /// <summary>Books on one shelf.</summary>
    [HttpGet("shelves/{shelfId:int}")]
    public async Task<IActionResult> Shelf(int shelfId, [FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        FeedResult(await feedService.GetShelfBooksAsync(Links, shelfId, page, cancellationToken));

    /// <summary>Search by title, author (including pseudonyms) and series.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        FeedResult(await feedService.SearchAsync(Links, q, page, cancellationToken));

    /// <summary>OpenSearch description document for the search feed.</summary>
    [HttpGet("opensearch.xml")]
    public IActionResult OpenSearch() =>
        Content(OpdsXmlSerializer.Serialize(feedService.GetOpenSearchDescription(Links)), $"{OpdsMediaTypes.OpenSearchDescription}; charset=utf-8");

    /// <summary>
    /// Downloads the book file. The trailing file name is cosmetic (some readers name the saved file
    /// from the URL); supports range requests so interrupted downloads can resume.
    /// </summary>
    [HttpGet("books/{bookId:int}/download/{fileName?}")]
    public async Task<IActionResult> Download(int bookId, CancellationToken cancellationToken)
    {
        var book = await FindAccessibleBookAsync(bookId, cancellationToken);
        if (book is null)
        {
            return NotFound();
        }

        if (!System.IO.File.Exists(book.FilePath))
        {
            logger.LogWarning("OPDS download for book {BookId} failed: file is missing on disk", bookId);
            return NotFound();
        }

        string fileName = BookFileNames.BuildDownloadFileName(book.Id, book.Title, book.FilePath, book.FileFormat);
        return PhysicalFile(book.FilePath, OpdsMediaTypes.ForFormat(book.FileFormat), fileName, enableRangeProcessing: true);
    }

    /// <summary>Full-size cover image.</summary>
    [HttpGet("books/{bookId:int}/cover")]
    public async Task<IActionResult> Cover(int bookId, CancellationToken cancellationToken)
    {
        string? coverPath = await FindCoverPathAsync(bookId, cancellationToken);
        return coverPath is null ? NotFound() : ImageResult(coverPath, MimeFromExtension(Path.GetExtension(coverPath)));
    }

    /// <summary>Small JPEG cover for catalogue listings; falls back to the full cover if it can't be resized.</summary>
    [HttpGet("books/{bookId:int}/thumbnail")]
    public async Task<IActionResult> Thumbnail(int bookId, CancellationToken cancellationToken)
    {
        string? coverPath = await FindCoverPathAsync(bookId, cancellationToken);
        if (coverPath is null)
        {
            return NotFound();
        }

        string? thumbnailPath = await thumbnailService.GetThumbnailPathAsync(bookId, coverPath, options.Value.ThumbnailSize, cancellationToken);
        return thumbnailPath is null
            ? ImageResult(coverPath, MimeFromExtension(Path.GetExtension(coverPath)))
            : ImageResult(thumbnailPath, "image/jpeg");
    }

    private OpdsLinkBuilder Links => new(Request.PathBase, RouteData.Values[OpdsAuthenticationHandler.ApiKeyRouteValue] as string);

    private IActionResult FeedResult(Result<OpdsFeed> result) => result.Status switch
    {
        ResultStatus.Ok => Content(OpdsXmlSerializer.Serialize(result.Value), $"{result.Value.MediaType}; charset=utf-8"),
        ResultStatus.NotFound => NotFound(),
        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };

    private IActionResult ImageResult(string path, string contentType)
    {
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Private = true,
            MaxAge = CoverCacheDuration,
        };

        return new PhysicalFileResult(path, contentType)
        {
            LastModified = System.IO.File.GetLastWriteTimeUtc(path),
        };
    }

    private sealed record BookFileRow(int Id, string Title, string FilePath, EbookFormat FileFormat, string? CoverImagePath, int ShelfId);

    private async Task<BookFileRow?> FindAccessibleBookAsync(int bookId, CancellationToken cancellationToken)
    {
        var book = await bookRepository.FindOneAsync(
            new SearchOptions<Book>
            {
                Query = b => b.Id == bookId,
                CancellationToken = cancellationToken,
            },
            b => new BookFileRow(b.Id, b.Title, b.FilePath, b.FileFormat, b.CoverImagePath, b.ShelfId));

        if (book is null || !await shelfAccessService.CanAccessShelfAsync(book.ShelfId, cancellationToken))
        {
            return null;
        }

        return book;
    }

    private async Task<string?> FindCoverPathAsync(int bookId, CancellationToken cancellationToken)
    {
        var book = await FindAccessibleBookAsync(bookId, cancellationToken);
        if (book is null || string.IsNullOrEmpty(book.CoverImagePath))
        {
            return null;
        }

        string fullPath = Path.Combine(storage.CoversDirectory, book.CoverImagePath);
        return System.IO.File.Exists(fullPath) ? fullPath : null;
    }

    private static string MimeFromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream",
    };
}
