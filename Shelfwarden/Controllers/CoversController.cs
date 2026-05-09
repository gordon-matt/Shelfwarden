using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Shelfwarden.Services.Storage;
using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;

namespace Shelfwarden.Controllers;

/// <summary>
/// Streams extracted cover images from <see cref="IStoragePathProvider.CoversDirectory"/>.
/// Files are stored as <c>{bookId}.{ext}</c>, but we deliberately do not trust the requested
/// extension — we look up the book's actual cover path and serve that.
/// </summary>
[ApiController]
[Authorize]
[Route("covers")]
public class CoversController(
    IStoragePathProvider storage,
    IShelfAccessService shelfAccessService,
    IRepository<Book> bookRepository) : ControllerBase
{
    [HttpGet("{bookId:int}")]
    public async Task<IActionResult> Get(int bookId, CancellationToken cancellationToken)
    {
        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });

        if (book is null || string.IsNullOrEmpty(book.CoverImagePath))
        {
            return NotFound();
        }

        if (!await shelfAccessService.CanAccessShelfAsync(book.ShelfId, cancellationToken))
        {
            return Forbid();
        }

        string fullPath = Path.Combine(storage.CoversDirectory, book.CoverImagePath);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        // Use the file's actual extension to set the content type — never trust the URL.
        string contentType = MimeFromExtension(Path.GetExtension(fullPath));

        // Keep covers revalidating so clients refresh quickly when cover files change.
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Private = true,
            MaxAge = TimeSpan.Zero,
            MustRevalidate = true,
        };

        // Tag the response with last-modified so browsers revalidate cheaply on the next scan.
        var lastModified = System.IO.File.GetLastWriteTimeUtc(fullPath);
        Response.GetTypedHeaders().LastModified = lastModified;

        var stream = System.IO.File.OpenRead(fullPath);
        return File(stream, contentType, enableRangeProcessing: false);
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