using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;

namespace Shelfwarden.Controllers;

/// <summary>
/// Serves extra content files (maps, interviews, PDFs, images, etc.) that are associated with
/// authors, series, and books. Files are stored in the extras directory on disk and served with
/// appropriate content-type headers. Authorization is required for all endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("extra-content")]
public class ExtraContentController(
    ILogger<ExtraContentController> logger,
    IAdditionalContentService contentService) : ControllerBase
{
    private static readonly Dictionary<string, string> ExtensionContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents
        { ".pdf", "application/pdf" },
        { ".txt", "text/plain; charset=utf-8" },
        { ".md", "text/markdown; charset=utf-8" },
        { ".html", "text/html; charset=utf-8" },
        { ".htm", "text/html; charset=utf-8" },
        { ".csv", "text/csv; charset=utf-8" },
        // Images
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".webp", "image/webp" },
        { ".bmp", "image/bmp" },
        { ".svg", "image/svg+xml" },
        // Archives / other
        { ".zip", "application/zip" },
        { ".epub", "application/epub+zip" },
        { ".mp3", "audio/mpeg" },
        { ".mp4", "video/mp4" },
        { ".mkv", "video/x-matroska" },
    };

    /// <summary>Serves (inline) or downloads an extra content file by its database id.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, [FromQuery] bool download, CancellationToken cancellationToken)
    {
        var result = await contentService.GetByIdAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return NotFound();
        }

        var item = result.Value;
        if (!System.IO.File.Exists(item.FilePath))
        {
            logger.LogWarning("Extra content item {Id} references missing file at '{Path}'", id, item.FilePath);
            return NotFound();
        }

        string ext = item.FileExtension.ToLowerInvariant();
        string contentType = ExtensionContentTypes.GetValueOrDefault(ext, MediaTypeNames.Application.Octet);
        string safeFileName = SanitizeFileName(item.FileName);

        var stream = System.IO.File.OpenRead(item.FilePath);

        if (download)
        {
            Response.Headers.ContentDisposition =
                $"attachment; filename=\"{System.Net.WebUtility.UrlEncode(safeFileName)}\"; filename*=UTF-8''{System.Net.WebUtility.UrlEncode(safeFileName)}";
            return File(stream, contentType, enableRangeProcessing: true);
        }

        return File(stream, contentType, enableRangeProcessing: true);
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(safe) ? "file" : safe;
    }
}