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
[Microsoft.AspNetCore.Mvc.Route("files")]
public class BookFilesController(
    ILogger<BookFilesController> logger,
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

        if (book is null || string.IsNullOrEmpty(book.FilePath))
        {
            return NotFound();
        }

        if (!System.IO.File.Exists(book.FilePath))
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning("Book {BookId} references missing file at {Path}", bookId, book.FilePath);
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
        var fileName = Path.GetFileName(book.FilePath);
        return File(stream, contentType, fileName, enableRangeProcessing: true);
    }
}
