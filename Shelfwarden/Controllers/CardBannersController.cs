using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Shelfwarden.Services.Storage;
using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;

namespace Shelfwarden.Controllers;

/// <summary>
/// Serves user-uploaded tile banner images for shelves, collections, and reading lists.
/// </summary>
[ApiController]
[Authorize]
[Route("card-banners")]
public class CardBannersController(
    IStoragePathProvider storage,
    IUserContextService userContext,
    IShelfAccessService shelfAccessService,
    IRepository<Collection> collectionRepository,
    IRepository<ReadingList> readingListRepository,
    IRepository<Shelf> shelfRepository) : ControllerBase
{
    [HttpGet("shelves/{id:int}")]
    public async Task<IActionResult> GetShelf(int id, CancellationToken cancellationToken)
    {
        var shelf = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = s => s.Id == id,
            CancellationToken = cancellationToken,
        });
        return shelf is null
            ? NotFound()
            : !await shelfAccessService.CanAccessShelfAsync(id, cancellationToken) ? NotFound() : StreamBanner("shelves", id);
    }

    [HttpGet("collections/{id:int}")]
    public async Task<IActionResult> GetCollection(int id, CancellationToken cancellationToken)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var collection = await collectionRepository.FindOneAsync(new SearchOptions<Collection>
        {
            Query = c => c.Id == id,
            CancellationToken = cancellationToken,
        });
        if (collection is null)
        {
            return NotFound();
        }

        // Shelfwarden.Core.Constants.GlobalUserId — avoid taking a Core dependency from this project.
        bool isGlobal = collection.OwnerUserId == "_global";
        return !isGlobal && collection.OwnerUserId != userId ? NotFound() : StreamBanner("collections", id);
    }

    [HttpGet("reading-lists/{id:int}")]
    public async Task<IActionResult> GetReadingList(int id, CancellationToken cancellationToken)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var list = await readingListRepository.FindOneAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.Id == id,
            CancellationToken = cancellationToken,
        });
        return list is null || list.OwnerUserId != userId ? NotFound() : StreamBanner("reading-lists", id);
    }

    private IActionResult StreamBanner(string kind, int id)
    {
        string? path = storage.FindCardBannerFilePath(kind, id);
        if (path is null || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Private = true,
            MaxAge = TimeSpan.FromHours(24),
        };

        string contentType = MimeFromExtension(Path.GetExtension(path));
        var stream = System.IO.File.OpenRead(path);
        return File(stream, contentType, enableRangeProcessing: true);
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