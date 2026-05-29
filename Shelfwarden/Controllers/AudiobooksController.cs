using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Shelfwarden.Services.Storage;
using RouteAttribute = Microsoft.AspNetCore.Mvc.RouteAttribute;

namespace Shelfwarden.Controllers;

/// <summary>
/// Streams generated audiobooks (.m4a). Completed books require authentication plus shelf access.
/// Voice previews require an administrator (same policy as starting generation).
/// </summary>
[ApiController]
[Authorize]
[Route("audiobooks")]
public class AudiobooksController(
    IRepository<Audiobook> audiobookRepository,
    IRepository<Book> bookRepository,
    IAudiobookService audiobookService,
    IShelfAccessService shelfAccessService,
    IStoragePathProvider storage) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("{bookId:int}")]
    public async Task<IActionResult> GetAudiobook(int bookId, CancellationToken cancellationToken)
    {
        var audiobook = await audiobookRepository.FindOneAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.BookId == bookId,
            CancellationToken = cancellationToken,
        });

        if (audiobook is null
            || audiobook.Status != AudiobookStatus.Completed
            || string.IsNullOrEmpty(audiobook.OutputFileName))
        {
            return NotFound();
        }

        if (!await shelfAccessService.CanAccessBookAsync(bookId, cancellationToken))
        {
            return Forbid();
        }

        string fullPath = storage.GetAudiobookFilePath(bookId);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        // Range requests let the browser scrub forward without downloading the whole file
        // first — important since audiobooks easily run into the hundreds of megabytes.
        var stream = System.IO.File.OpenRead(fullPath);
        return File(stream, "audio/mp4", enableRangeProcessing: true);
    }

    [HttpGet("{bookId:int}/chapters/{index:int}")]
    public async Task<IActionResult> GetChapter(int bookId, int index, CancellationToken cancellationToken)
    {
        var audiobook = await audiobookRepository.FindOneAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.BookId == bookId,
            CancellationToken = cancellationToken,
        });

        if (audiobook is null
            || audiobook.Status != AudiobookStatus.Completed
            || !audiobook.SplitByChapter)
        {
            return NotFound();
        }

        if (!await shelfAccessService.CanAccessBookAsync(bookId, cancellationToken))
        {
            return Forbid();
        }

        string fullPath = storage.GetAudiobookChapterFilePath(bookId, index);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var stream = System.IO.File.OpenRead(fullPath);
        return File(stream, "audio/mp4", enableRangeProcessing: true);
    }

    /// <summary>
    /// Streams every produced file for a book as a single ZIP. Works for both a single-file
    /// audiobook and a split (per-chapter) one. Members are stored uncompressed because AAC is
    /// already compressed — re-deflating would burn CPU for almost no size gain.
    /// </summary>
    [HttpGet("{bookId:int}/zip")]
    public async Task<IActionResult> GetZip(int bookId, CancellationToken cancellationToken)
    {
        var audiobook = await audiobookRepository.FindOneAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.BookId == bookId,
            CancellationToken = cancellationToken,
        });

        if (audiobook is null || audiobook.Status != AudiobookStatus.Completed)
        {
            return NotFound();
        }

        if (!await shelfAccessService.CanAccessBookAsync(bookId, cancellationToken))
        {
            return Forbid();
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });
        string bookName = SanitizeFileName(book?.Title ?? $"audiobook-{bookId}");

        var members = ResolveZipMembers(audiobook, bookId, bookName);
        if (members.Count == 0)
        {
            return NotFound();
        }

        // ZipArchive finalises the central directory with synchronous writes on Dispose, which
        // Kestrel/IIS reject when writing directly to Response.Body. Build on disk first.
        string tempZip = Path.Combine(Path.GetTempPath(), $"shelfwarden-{bookId}-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var zipStream = new FileStream(
                tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var (path, entryName) in members)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                    await using var entryStream = entry.Open();
                    await using var fileStream = new FileStream(
                        path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                    await fileStream.CopyToAsync(entryStream, cancellationToken);
                }
            }

            var downloadStream = new FileStream(
                tempZip,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);

            return File(downloadStream, "application/zip", $"{bookName}.zip");
        }
        catch
        {
            if (System.IO.File.Exists(tempZip))
            {
                try
                {
                    System.IO.File.Delete(tempZip);
                }
                catch
                {
                    // Best-effort cleanup if building or opening the download stream failed.
                }
            }

            throw;
        }
    }

    private List<(string Path, string EntryName)> ResolveZipMembers(Audiobook audiobook, int bookId, string bookName)
    {
        var members = new List<(string, string)>();

        if (audiobook.SplitByChapter && !string.IsNullOrWhiteSpace(audiobook.ChaptersJson))
        {
            List<AudiobookChapterDto>? chapters = null;
            try
            {
                chapters = JsonSerializer.Deserialize<List<AudiobookChapterDto>>(audiobook.ChaptersJson, JsonOptions);
            }
            catch (JsonException)
            {
                // Fall through to whatever files exist on disk below.
            }

            if (chapters is { Count: > 0 })
            {
                foreach (var chapter in chapters)
                {
                    string path = storage.GetAudiobookChapterFilePath(bookId, chapter.Index);
                    if (System.IO.File.Exists(path))
                    {
                        string title = SanitizeFileName(chapter.Title);
                        members.Add((path, $"{chapter.Index + 1:D3} - {title}.m4a"));
                    }
                }

                return members;
            }
        }

        string single = storage.GetAudiobookFilePath(bookId);
        if (System.IO.File.Exists(single))
        {
            members.Add((single, $"{bookName}.m4a"));
        }

        return members;
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        clean = clean.Trim().TrimEnd('.');
        if (clean.Length > 120)
        {
            clean = clean[..120].Trim();
        }
        return clean.Length == 0 ? "audiobook" : clean;
    }

    [HttpGet("voices/{voiceName}/preview")]
    [Authorize(Roles = Constants.Roles.Administrator)]
    public async Task<IActionResult> GetVoicePreview(string voiceName, CancellationToken cancellationToken)
    {
        var result = await audiobookService.EnsureVoiceSampleAsync(voiceName, cancellationToken);
        if (result.IsNotFound())
        {
            return NotFound();
        }
        if (!result.IsSuccess)
        {
            return Problem(result.Errors.FirstOrDefault() ?? "Could not generate preview.");
        }

        string path = result.Value;
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var stream = System.IO.File.OpenRead(path);
        return File(stream, "audio/wav");
    }
}