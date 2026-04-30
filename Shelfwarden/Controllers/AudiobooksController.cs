using Microsoft.AspNetCore.Mvc;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Controllers;

/// <summary>
/// Streams generated audiobooks (.m4a) and the cached short voice-preview WAVs. Both flows
/// require authentication so audiobook URLs aren't accidentally usable as a public download
/// link, and both lean on the storage abstraction so no consumer ever builds a path from
/// configuration directly.
/// </summary>
[ApiController]
[Authorize]
[Route("audiobooks")]
public class AudiobooksController(
    IRepository<Audiobook> audiobookRepository,
    IAudiobookService audiobookService,
    IStoragePathProvider storage) : ControllerBase
{
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

    [HttpGet("voices/{voiceName}/preview")]
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
