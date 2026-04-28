using Microsoft.AspNetCore.Mvc;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Controllers;

/// <summary>
/// Streams extracted cover images from <see cref="IStoragePathProvider.CoversDirectory"/>.
/// Files are stored as <c>{bookId}.{ext}</c>, but we deliberately do not trust the requested
/// extension — we look up the book's actual cover path and serve that. Caches aggressively
/// since cover bytes are content-addressable per (book, scan).
/// </summary>
[ApiController]
[Authorize]
[Route("covers")]
public class CoversController(
    IStoragePathProvider storage,
    IRepository<Book> bookRepository) : ControllerBase
{
    [HttpGet("{bookId:int}")]
    [ResponseCache(Duration = 60 * 60 * 24 * 7, Location = ResponseCacheLocation.Any)]
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

        string fullPath = Path.Combine(storage.CoversDirectory, book.CoverImagePath);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        // Use the file's actual extension to set the content type — never trust the URL.
        string contentType = MimeFromExtension(Path.GetExtension(fullPath));

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