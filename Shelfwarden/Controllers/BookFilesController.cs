using Microsoft.AspNetCore.Mvc;

namespace Shelfwarden.Controllers;

/// <summary>
/// Streams the raw ebook bytes (EPUB / PDF) to the client-side reader. We deliberately do not
/// expose the on-disk path or trust the URL extension — the path stored on the <see cref="Book"/>
/// row is the source of truth and we serve only that file with a content-type derived from the
/// known <see cref="EbookFormat"/>. Authorization is required so users can only read books they
/// can see in the catalog.
/// </summary>
[ApiController]
[Authorize]
[Route("files")]
public class BookFilesController(
    ILogger<BookFilesController> logger,
    IShelfAccessService shelfAccessService,
    IRepository<Book> bookRepository) : ControllerBase
{
    [HttpGet("{bookId:int}")]
    public async Task<IActionResult> Get(int bookId, [FromQuery] bool download, CancellationToken cancellationToken)
    {
        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });

        if (book is null || string.IsNullOrEmpty(book.FilePath))
        {
            return NotFound();
        }

        if (!await shelfAccessService.CanAccessShelfAsync(book.ShelfId, cancellationToken))
        {
            return Forbid();
        }

        if (!System.IO.File.Exists(book.FilePath))
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Book {BookId} references missing file at {Path}", bookId, book.FilePath);
            }

            return NotFound();
        }

        string contentType = book.FileFormat switch
        {
            EbookFormat.Epub => "application/epub+zip",
            EbookFormat.Pdf => "application/pdf",
            _ => "application/octet-stream",
        };

        // Range processing lets the browser stream large PDFs without buffering the whole file
        // into memory. epub.js also uses ranged requests for big EPUBs (downloads a chapter
        // at a time).
        var stream = System.IO.File.OpenRead(book.FilePath);
        string downloadName = BuildDownloadFileName(book);

        if (download)
        {
            // Force a Save-As prompt rather than letting the browser open the file inline.
            // The Title-derived filename is friendlier than the on-disk path component.
            Response.Headers.ContentDisposition =
                $"attachment; filename=\"{System.Net.WebUtility.UrlEncode(downloadName)}\"; filename*=UTF-8''{System.Net.WebUtility.UrlEncode(downloadName)}";
            return File(stream, contentType, enableRangeProcessing: true);
        }

        return File(stream, contentType, downloadName, enableRangeProcessing: true);
    }

    private static string BuildDownloadFileName(Book book)
    {
        string ext = Path.GetExtension(book.FilePath);
        if (string.IsNullOrEmpty(ext))
        {
            ext = book.FileFormat switch
            {
                EbookFormat.Epub => ".epub",
                EbookFormat.Pdf => ".pdf",
                _ => string.Empty,
            };
        }

        string baseName = string.IsNullOrWhiteSpace(book.Title)
            ? Path.GetFileNameWithoutExtension(book.FilePath)
            : book.Title;

        // Strip filesystem-illegal characters so this round-trips into Save-As cleanly.
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitised = new string([.. baseName.Where(c => !invalid.Contains(c))]).Trim();
        if (string.IsNullOrEmpty(sanitised))
        {
            sanitised = $"book-{book.Id}";
        }

        return sanitised + ext;
    }
}