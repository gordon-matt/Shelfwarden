using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Shelfwarden.Services.Storage;
using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;

namespace Shelfwarden.Controllers;

/// <summary>
/// Streams author profile photos from <see cref="IStoragePathProvider.AuthorPhotosDirectory"/>.
/// Files are named <c>{authorId}.{ext}</c>. We deliberately don't allow an extension in the URL
/// — the server is the source of truth for what photo (if any) belongs to an author. When the
/// requested author is a pseudonym (<see cref="Author.PrimaryAuthorId"/>) and has no photo of
/// their own, the primary author's photo is served instead. Returns 404 when no photo is present
/// so the page can fall back to a placeholder.
/// </summary>
[ApiController]
[Authorize]
[Route("author-photos")]
public class AuthorPhotosController(
    IStoragePathProvider storage,
    IRepository<Author> authorRepository) : ControllerBase
{
    [HttpGet("{authorId:int}")]
    public async Task<IActionResult> Get(int authorId, CancellationToken cancellationToken)
    {
        string? fullPath = await ResolveAuthorPhotoPathAsync(authorId, cancellationToken);
        if (fullPath is null || !System.IO.File.Exists(fullPath))
        {
            // Never let a "no photo yet" 404 get cached. A 404 is cacheable by default per
            // RFC 7231, and Electron/Chromium's HTTP disk cache persists across app restarts.
            // Without this, the very first request for a not-yet-uploaded photo (?v=0) gets
            // permanently cached, so an uploaded photo never actually loads once one exists —
            // it looks like the photo was "lost" after closing and reopening the app.
            Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { NoStore = true };
            return NotFound();
        }

        string contentType = MimeFromExtension(Path.GetExtension(fullPath));

        var lastModified = System.IO.File.GetLastWriteTimeUtc(fullPath);
        Response.GetTypedHeaders().LastModified = lastModified;
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromDays(7),
        };

        var stream = System.IO.File.OpenRead(fullPath);
        return File(stream, contentType, enableRangeProcessing: false);
    }

    private async Task<string?> ResolveAuthorPhotoPathAsync(int authorId, CancellationToken cancellationToken)
    {
        string? fullPath = storage.FindAuthorPhotoPath(authorId);
        if (fullPath is not null)
        {
            return fullPath;
        }

        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == authorId,
            CancellationToken = cancellationToken,
        });

        if (author?.PrimaryAuthorId is int primaryId)
        {
            return storage.FindAuthorPhotoPath(primaryId);
        }

        return null;
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