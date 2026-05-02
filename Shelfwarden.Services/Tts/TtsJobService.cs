using System.Diagnostics;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// The Hangfire-driven audiobook synthesiser. Runs end-to-end on a TTS worker thread:
/// loads the book, extracts text, chunks it, synthesises chunk-by-chunk through Kokoro
/// (resuming from already-completed PCM files on disk), and finally encodes the merged
/// stream to .m4a via FFmpeg. Failures persist as <see cref="AudiobookStatus.Failed"/> on
/// the row so the UI can surface them.
/// </summary>
public sealed class TtsJobService(
    ILogger<TtsJobService> logger,
    IRepository<Book> bookRepository,
    IRepository<Audiobook> audiobookRepository,
    IBookTextExtractorFactory textExtractorFactory,
    IKokoroEngineProvider kokoroProvider,
    IAudiobookProgressTracker progressTracker,
    AudioStitcher audioStitcher,
    IStoragePathProvider storage) : ITtsJobService
{
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

        var extractor = textExtractorFactory.GetFor(book.FilePath)
            ?? throw new InvalidOperationException(
                $"No text extractor available for '{book.FilePath}'. Only EPUB and PDF are supported.");

        if (!File.Exists(book.FilePath))
        {
            throw new FileNotFoundException($"Book file '{book.FilePath}' is missing on disk.");
        }

        // Materialise chunks up front. Even a 100k-word book is well under 1 MB of plain
        // text, so the memory cost is fine — and we need the total count to drive the
        // progress bar deterministically.
        progressTracker.Update(book.Id, p => p.CurrentStage = "Extracting text");
        var chunks = await BuildChunksAsync(extractor, book.FilePath, cancellationToken);
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("The book contains no readable text.");
        }

        audiobook.TotalChunks = chunks.Count;
        audiobook.CompletedChunks = 0;
        await audiobookRepository.UpdateAsync(audiobook);
        progressTracker.Update(book.Id, p =>
        {
            p.TotalChunks = chunks.Count;
            p.CompletedChunks = 0;
            p.CurrentStage = "Loading TTS model";
        });

        var voice = kokoroProvider.FindVoice(audiobook.VoiceName)
            ?? throw new InvalidOperationException($"Voice '{audiobook.VoiceName}' is not loaded.");
        var engine = await kokoroProvider.GetEngineAsync(cancellationToken);

        string workingDir = storage.GetAudiobookWorkingDirectory(book.Id);
        string langCode = voice.GetLangCode();

        progressTracker.Update(book.Id, p => p.CurrentStage = "Synthesising audio");

        // Generate chunks sequentially. Kokoro's engine internally serialises jobs on a
        // single worker thread anyway, and stepping through one chunk at a time keeps the
        // resume story dead simple — every PCM file on disk is a completed unit of work.
        var pcmFiles = new List<string>(chunks.Count);
        int alreadyDone = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await WasCanceledByUserAsync(book.Id, cancellationToken))
            {
                return;
            }

            string pcmPath = Path.Combine(
                workingDir,
                $"chunk_{i:D6}.pcm");
            pcmFiles.Add(pcmPath);

            if (File.Exists(pcmPath) && new FileInfo(pcmPath).Length > 0)
            {
                alreadyDone++;
                ReportProgress(book.Id, audiobook, i + 1, chunks.Count);
                continue;
            }

            float[] samples = await SynthesiseChunkAsync(engine, chunks[i], voice, langCode, cancellationToken);
            await AudioStitcher.WritePcmChunkAsync(samples, pcmPath, cancellationToken);

            ReportProgress(book.Id, audiobook, i + 1, chunks.Count);
        }

        if (alreadyDone > 0)
        {
            logger.LogInformation(
                "Resumed {Done}/{Total} previously-generated chunks for book {BookId}",
                alreadyDone, chunks.Count, book.Id);
        }

        if (await WasCanceledByUserAsync(book.Id, cancellationToken))
        {
            return;
        }

        progressTracker.Update(book.Id, p =>
        {
            // User-visible — surfaced in BookDetail while FFmpeg merges PCM + encodes AAC.
            p.CurrentStage = "Stitching and encoding";
        });

        string outputPath = storage.GetAudiobookFilePath(book.Id);
        var encoded = await audioStitcher.EncodeAsync(pcmFiles, outputPath, cancellationToken);

        // Encoding succeeded — clean up the per-chunk PCM files. We leave the working dir
        // itself in place because StoragePathProvider re-creates it deterministically.
        foreach (string pcm in pcmFiles)
        {
            try
            {
                if (File.Exists(pcm))
                {
                    File.Delete(pcm);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to delete chunk file {Path}", pcm);
            }
        }

        sw.Stop();

        audiobook.Status = AudiobookStatus.Completed;
        audiobook.OutputFileName = Path.GetFileName(encoded.FilePath);
        audiobook.OutputSizeBytes = encoded.FileSizeBytes;
        audiobook.DurationSeconds = encoded.DurationSeconds > 0
            ? encoded.DurationSeconds
            : sw.Elapsed.TotalSeconds;
        audiobook.CompletedAt = DateTime.UtcNow;
        audiobook.CompletedChunks = chunks.Count;
        await audiobookRepository.UpdateAsync(audiobook);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Generated audiobook for book {BookId} ({Title}) in {Duration:c} -> {Path} ({Size} bytes)",
                book.Id, book.Title, sw.Elapsed, encoded.FilePath, encoded.FileSizeBytes);
        }
    }

    private static async Task<List<string>> BuildChunksAsync(
        IBookTextExtractor extractor,
        string filePath,
        CancellationToken cancellationToken)
    {
        var paragraphs = new List<string>();
        await foreach (string paragraph in extractor.ExtractAsync(filePath, cancellationToken))
        {
            paragraphs.Add(paragraph);
        }

        return TextChunker.ChunkParagraphs(paragraphs).ToList();
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
        // The in-memory tracker drives the UI in normal operation; the DB row is the
        // recovery surface.
        if (completed == total || completed % 10 == 0)
        {
            audiobook.CompletedChunks = completed;
            try
            {
                audiobookRepository.UpdateAsync(audiobook).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Row may have been deleted mid-job (user canceled).
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
            // Tokeniser dropped everything (e.g. a chunk consisting of only punctuation).
            // Emit a brief silence rather than skipping the chunk so the chunk count still
            // matches what the rest of the pipeline saw.
            return new float[(int)(AudioStitcher.SampleRateHz * 0.2)];
        }

        // Kokoro's per-step token ceiling is 510 — anything longer would be silently
        // truncated. Keep TextChunker's default well under that, but defend in depth here.
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
}