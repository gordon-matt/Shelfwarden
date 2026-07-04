using System.Diagnostics;
using System.Text.Json;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// The Hangfire-driven audiobook synthesiser. Runs end-to-end on a TTS worker thread:
/// loads the book, resolves the chapter plan (skip front matter / split per chapter),
/// extracts + chunks each render unit, synthesises chunk-by-chunk through Kokoro (resuming
/// from already-completed PCM files on disk), and finally encodes each unit to .m4a via
/// FFmpeg. A whole-book plan produces a single file; a split plan produces one file per
/// chapter. Failures persist as <see cref="AudiobookStatus.Failed"/> on the row.
/// </summary>
public sealed class TtsJobService(
    ILogger<TtsJobService> logger,
    IRepository<Book> bookRepository,
    IRepository<Audiobook> audiobookRepository,
    IEbookSectionParserFactory sectionParserFactory,
    IKokoroEngineProvider kokoroProvider,
    IAudiobookProgressTracker progressTracker,
    AudioStitcher audioStitcher,
    IStoragePathProvider storage) : ITtsJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task GenerateAsync(int bookId, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var audiobook = await audiobookRepository.FindOneAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.BookId == bookId,
        });
        if (audiobook is null)
        {
            logger.LogWarning("TTS job dispatched for book {BookId} but no Audiobook row exists; aborting", bookId);
            return;
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
        });
        if (book is null)
        {
            await MarkFailedAsync(audiobook, "Book no longer exists.", sw);
            return;
        }

        try
        {
            await RunPipelineAsync(book, audiobook, sw, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("TTS job for book {BookId} was canceled", bookId);
            await MarkFailedAsync(audiobook, "Generation was canceled.", sw);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TTS job for book {BookId} failed", bookId);
            await MarkFailedAsync(audiobook, ex.Message, sw);
            throw;
        }
        finally
        {
            progressTracker.Finish(bookId);
        }
    }

    private async Task RunPipelineAsync(
        Book book,
        Audiobook audiobook,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        if (await WasCanceledByUserAsync(book.Id, cancellationToken))
        {
            return;
        }

        // Read before the reset below so we can recover durations of chapters that already
        // exist on disk when resuming a partially-completed split generation.
        var priorChapters = DeserializeChapters(audiobook.ChaptersJson);

        progressTracker.Start(book.Id, audiobook.VoiceName);
        progressTracker.Update(book.Id, p => p.CurrentStage = "Preparing");

        audiobook.Status = AudiobookStatus.Running;
        audiobook.StartedAt = DateTime.UtcNow;
        audiobook.ErrorMessage = null;
        audiobook.OutputFileName = null;
        audiobook.OutputSizeBytes = null;
        audiobook.DurationSeconds = null;
        audiobook.CompletedAt = null;
        await audiobookRepository.UpdateAsync(audiobook);

        var parser = sectionParserFactory.GetFor(book.FilePath)
            ?? throw new InvalidOperationException(
                $"No section parser available for '{book.FilePath}'. Only EPUB and PDF are supported.");

        if (!File.Exists(book.FilePath))
        {
            throw new FileNotFoundException($"Book file '{book.FilePath}' is missing on disk.");
        }

        var units = ResolveRenderUnits(audiobook, book.Title);
        bool split = audiobook.SplitByChapter && units.Count > 0;

        // Materialise chunks per unit up front so the progress bar total is deterministic. Even a
        // 100k-word book is well under 1 MB of plain text, so the memory cost is negligible.
        progressTracker.Update(book.Id, p => p.CurrentStage = "Extracting text");
        var unitChunks = new List<List<string>>(units.Count);
        int totalChunks = 0;
        foreach (var unit in units)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunks = await BuildChunksAsync(parser, book.FilePath, unit.Sections, cancellationToken);
            unitChunks.Add(chunks);
            totalChunks += chunks.Count;
        }

        if (totalChunks == 0)
        {
            throw new InvalidOperationException("The selected sections contain no readable text.");
        }

        audiobook.TotalChunks = totalChunks;
        audiobook.CompletedChunks = 0;
        await audiobookRepository.UpdateAsync(audiobook);
        progressTracker.Update(book.Id, p =>
        {
            p.TotalChunks = totalChunks;
            p.CompletedChunks = 0;
            p.CurrentStage = "Loading TTS model";
        });

        var voice = kokoroProvider.FindVoice(audiobook.VoiceName)
            ?? throw new InvalidOperationException($"Voice '{audiobook.VoiceName}' is not loaded.");
        var engine = await kokoroProvider.GetEngineAsync(cancellationToken);
        string langCode = voice.GetLangCode();

        progressTracker.Update(book.Id, p => p.CurrentStage = "Synthesising audio");

        var results = new List<ChapterResult>(units.Count);
        int globalCompleted = 0;

        for (int u = 0; u < units.Count; u++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await WasCanceledByUserAsync(book.Id, cancellationToken))
            {
                return;
            }

            var chunks = unitChunks[u];
            string outputPath = split
                ? storage.GetAudiobookChapterFilePath(book.Id, u)
                : storage.GetAudiobookFilePath(book.Id);

            // Chapter-level resume: an encoded file on disk means this unit is already done
            // (its PCM chunks were deleted after encoding), so skip straight past it.
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
            {
                globalCompleted += chunks.Count;
                ReportProgress(book.Id, audiobook, globalCompleted, totalChunks);
                double priorDuration = priorChapters.FirstOrDefault(c => c.Index == u)?.DurationSeconds ?? 0d;
                results.Add(new ChapterResult(u, units[u].Title, new FileInfo(outputPath).Length, priorDuration));
                continue;
            }

            string workingDir = split
                ? storage.GetAudiobookChapterWorkingDirectory(book.Id, u)
                : storage.GetAudiobookWorkingDirectory(book.Id);

            var pcmFiles = new List<string>(chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await WasCanceledByUserAsync(book.Id, cancellationToken))
                {
                    return;
                }

                string pcmPath = Path.Combine(workingDir, $"chunk_{i:D6}.pcm");
                pcmFiles.Add(pcmPath);

                if (File.Exists(pcmPath) && new FileInfo(pcmPath).Length > 0)
                {
                    globalCompleted++;
                    ReportProgress(book.Id, audiobook, globalCompleted, totalChunks);
                    continue;
                }

                float[] samples = await SynthesiseChunkAsync(engine, chunks[i], voice, langCode, cancellationToken);
                await AudioStitcher.WritePcmChunkAsync(samples, pcmPath, cancellationToken);

                globalCompleted++;
                ReportProgress(book.Id, audiobook, globalCompleted, totalChunks);
            }

            if (await WasCanceledByUserAsync(book.Id, cancellationToken))
            {
                return;
            }

            int unitNumber = u + 1;
            progressTracker.Update(book.Id, p => p.CurrentStage = split
                ? $"Stitching and encoding (chapter {unitNumber}/{units.Count})"
                : "Stitching and encoding");

            var encoded = await audioStitcher.EncodeAsync(pcmFiles, outputPath, cancellationToken);

            foreach (string pcm in pcmFiles)
            {
                TryDelete(pcm);
            }

            results.Add(new ChapterResult(u, units[u].Title, encoded.FileSizeBytes, encoded.DurationSeconds));

            // Persist chapter metadata as we go so a crash mid-book doesn't lose finished chapters.
            if (split)
            {
                audiobook.ChaptersJson = SerializeChapters(results);
                await audiobookRepository.UpdateAsync(audiobook);
            }
        }

        sw.Stop();

        audiobook.Status = AudiobookStatus.Completed;
        audiobook.CompletedChunks = totalChunks;
        audiobook.CompletedAt = DateTime.UtcNow;

        if (split)
        {
            audiobook.OutputFileName = null;
            audiobook.ChaptersJson = SerializeChapters(results);
            audiobook.OutputSizeBytes = results.Sum(r => r.SizeBytes);
            double durationSum = results.Sum(r => r.DurationSeconds);
            audiobook.DurationSeconds = durationSum > 0 ? durationSum : sw.Elapsed.TotalSeconds;
        }
        else
        {
            var single = results[0];
            audiobook.OutputFileName = Path.GetFileName(storage.GetAudiobookFilePath(book.Id));
            audiobook.ChaptersJson = null;
            audiobook.OutputSizeBytes = single.SizeBytes;
            audiobook.DurationSeconds = single.DurationSeconds > 0 ? single.DurationSeconds : sw.Elapsed.TotalSeconds;
        }

        await audiobookRepository.UpdateAsync(audiobook);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Generated audiobook for book {BookId} ({Title}) in {Duration:c} — {Units} file(s), {Size} bytes",
                book.Id, book.Title, sw.Elapsed, results.Count, audiobook.OutputSizeBytes);
        }
    }

    /// <summary>
    /// Turn the persisted section plan into the ordered list of render units (each becomes one
    /// audio file). A null/empty plan means "the whole book" (a single unit). When chapter
    /// splitting is on, included sections are grouped on their chapter-boundary flag.
    /// </summary>
    private List<RenderUnit> ResolveRenderUnits(Audiobook audiobook, string bookTitle)
    {
        var plan = DeserializePlan(audiobook.SectionPlanJson);
        if (plan is null || plan.Count == 0)
        {
            return [new RenderUnit(bookTitle, null)];
        }

        var included = plan.Where(s => s.IsIncluded).ToList();
        if (included.Count == 0)
        {
            // The UI prevents this, but never synthesise nothing — fall back to the whole plan.
            included = plan;
        }

        if (!audiobook.SplitByChapter)
        {
            return [new RenderUnit(bookTitle, included)];
        }

        var units = new List<RenderUnit>();
        List<BookSection>? current = null;
        foreach (var section in included)
        {
            if (current is null || section.IsChapterBoundary)
            {
                current = [];
                units.Add(new RenderUnit(
                    string.IsNullOrWhiteSpace(section.Title) ? $"Chapter {units.Count + 1}" : section.Title.Trim(),
                    current));
            }

            current.Add(section);
        }

        return units;
    }

    private static async Task<List<string>> BuildChunksAsync(
        IEbookSectionParser parser,
        string filePath,
        IReadOnlyList<BookSection>? sections,
        CancellationToken cancellationToken)
    {
        var paragraphs = new List<string>();
        await foreach (string paragraph in parser.ExtractAsync(filePath, sections, cancellationToken))
        {
            paragraphs.Add(paragraph);
        }

        // PDFs are emitted one page at a time, so a sentence that spans a page break would otherwise be
        // split into two chunks and read as two sentences with a pause. Chunk PDFs as a continuous
        // stream that carries the unfinished sentence across the boundary. EPUBs already emit whole
        // paragraphs, so their existing per-paragraph pauses are preserved.
        return parser.Format == EbookFormat.Pdf
            ? TextChunker.ChunkContinuous(paragraphs).ToList()
            : TextChunker.ChunkParagraphs(paragraphs).ToList();
    }

    /// <summary>
    /// User canceled via <see cref="AudiobookService.CancelAsync"/> — row is deleted so the
    /// worker must exit without writing a Failed state (nothing left to update).
    /// </summary>
    private async Task<bool> WasCanceledByUserAsync(int bookId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var row = await audiobookRepository.FindOneAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.BookId == bookId,
            CancellationToken = cancellationToken,
        });

        if (row is null)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Audiobook row for book {BookId} was removed — stopping job (user canceled)",
                    bookId);
            }

            progressTracker.Finish(bookId);
            return true;
        }

        return false;
    }

    private void ReportProgress(int bookId, Audiobook audiobook, int completed, int total)
    {
        progressTracker.Update(bookId, p =>
        {
            p.TotalChunks = total;
            p.CompletedChunks = completed;
        });

        // Persist every ~10 chunks so a process crash doesn't lose all visible progress.
        if (completed == total || completed % 10 == 0)
        {
            audiobook.CompletedChunks = completed;
            try
            {
                audiobookRepository.UpdateAsync(audiobook).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not persist chunk progress for book {BookId}", bookId);
            }
        }
    }

    private async Task MarkFailedAsync(Audiobook audiobook, string message, Stopwatch sw)
    {
        sw.Stop();
        audiobook.Status = AudiobookStatus.Failed;
        audiobook.ErrorMessage = string.IsNullOrWhiteSpace(message) ? "Unknown error." : message[..Math.Min(message.Length, 2000)];
        audiobook.CompletedAt = DateTime.UtcNow;
        audiobook.DurationSeconds = sw.Elapsed.TotalSeconds;
        try
        {
            await audiobookRepository.UpdateAsync(audiobook);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Failed audiobook status for book {BookId}", audiobook.BookId);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to delete chunk file {Path}", path);
        }
    }

    private static List<BookSection>? DeserializePlan(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<BookSection>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<AudiobookChapterDto> DeserializeChapters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AudiobookChapterDto>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string SerializeChapters(IEnumerable<ChapterResult> results)
        => JsonSerializer.Serialize(
            results.Select(r => new AudiobookChapterDto(r.Index, r.Title, r.SizeBytes, r.DurationSeconds)).ToList(),
            JsonOptions);

    /// <summary>
    /// Tokenize the text, dispatch a single-step Kokoro inference job, and await the audio
    /// samples. We deliberately bypass <c>tts.Speak</c> so no playback is wired up — this
    /// runs on a server / NAS where audio output isn't required (and may not even exist).
    /// </summary>
    internal static async Task<float[]> SynthesiseChunkAsync(
        KokoroTTS engine,
        string chunkText,
        KokoroVoice voice,
        string langCode,
        CancellationToken cancellationToken)
    {
        int[] tokens = Tokenizer.Tokenize(chunkText.Trim(), langCode, preprocess: true);
        if (tokens.Length == 0)
        {
            return new float[(int)(AudioStitcher.SampleRateHz * 0.2)];
        }

        if (tokens.Length > 500)
        {
            tokens = tokens[..500];
        }

        var tcs = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = KokoroJob.Create(tokens, voice, speed: 1f, samples => tcs.TrySetResult(samples ?? []));

        engine.EnqueueJob(job);

        using (cancellationToken.Register(() =>
        {
            job.Cancel();
            tcs.TrySetCanceled(cancellationToken);
        }))
        {
            return await tcs.Task;
        }
    }

    /// <summary>One audio file to render: a title plus the sections it covers (null = whole book).</summary>
    private sealed record RenderUnit(string Title, IReadOnlyList<BookSection>? Sections);

    /// <summary>The outcome of encoding a single render unit.</summary>
    private sealed record ChapterResult(int Index, string Title, long SizeBytes, double DurationSeconds);
}
