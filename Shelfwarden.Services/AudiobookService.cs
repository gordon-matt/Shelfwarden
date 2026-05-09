using Hangfire;
using KokoroSharp.Core;
using Shelfwarden.Services.Storage;
using Shelfwarden.Services.Tts;

namespace Shelfwarden.Services;

/// <summary>
/// Default <see cref="IAudiobookService"/>. Schedules <see cref="ITtsJobService"/> Hangfire
/// jobs and merges the persisted <c>Audiobook</c> row with the in-process
/// <see cref="IAudiobookProgressTracker"/> snapshot when responding to status polls — same
/// pattern as <c>IScanStatusService</c>.
/// </summary>
public class AudiobookService(
    ILogger<AudiobookService> logger,
    IUserContextService userContext,
    IBackgroundJobClient backgroundJobs,
    IRepository<Book> bookRepository,
    IRepository<Audiobook> audiobookRepository,
    IKokoroEngineProvider kokoroProvider,
    IAudiobookProgressTracker progressTracker,
    IStoragePathProvider storage) : IAudiobookService
{
    private const string SampleSentence = "The quick brown fox jumps over the lazy dog.";

    public async Task<Result<AudiobookDto>> GetStatusAsync(int bookId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated())
        {
            return Result.Unauthorized();
        }

        var existing = await audiobookRepository.FindOneAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.BookId == bookId,
            CancellationToken = cancellationToken,
        });

        return existing is null
            ? Result.Success(new AudiobookDto(
                bookId,
                AudiobookState.None,
                VoiceName: string.Empty,
                TotalChunks: 0,
                CompletedChunks: 0,
                PercentComplete: null,
                CurrentStage: null,
                OutputSizeBytes: null,
                DurationSeconds: null,
                ErrorMessage: null,
                CreatedAt: default,
                StartedAt: null,
                CompletedAt: null))
            : Result.Success(MergeWithLiveProgress(existing));
    }

    public async Task<Result<AudiobookDto>> GenerateAsync(int bookId, GenerateAudiobookRequest request, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });
        if (book is null)
        {
            return Result.NotFound();
        }

        if (book.FileFormat is not EbookFormat.Epub and not EbookFormat.Pdf)
        {
            return Result.Invalid(new ValidationError(nameof(book.FileFormat),
                "Only EPUB and PDF books support text-to-speech."));
        }

        var voice = kokoroProvider.FindVoice(request.VoiceName);
        if (voice is null)
        {
            return Result.Invalid(new ValidationError(nameof(request.VoiceName),
                $"Unknown voice '{request.VoiceName}'."));
        }

        var existing = await audiobookRepository.FindOneAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.BookId == bookId,
        });

        if (existing is { Status: AudiobookStatus.Pending or AudiobookStatus.Running })
        {
            return Result.Conflict("An audiobook is already being generated for this book.");
        }

        // If the user picked a different voice we treat the previous output as stale and
        // clear the partial work — chunks made with voice A would sound jarring spliced into
        // a finished file made with voice B.
        bool voiceChanged = existing is not null
            && !string.Equals(existing.VoiceName, request.VoiceName, StringComparison.OrdinalIgnoreCase);
        if (voiceChanged)
        {
            storage.DeleteAudiobook(bookId);
        }

        Audiobook audiobook;
        if (existing is null)
        {
            audiobook = await audiobookRepository.InsertAsync(new Audiobook
            {
                BookId = bookId,
                Status = AudiobookStatus.Pending,
                VoiceName = request.VoiceName,
                RequestedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Status = AudiobookStatus.Pending;
            existing.VoiceName = request.VoiceName;
            existing.RequestedByUserId = userId;
            existing.ErrorMessage = null;
            existing.OutputFileName = voiceChanged ? null : existing.OutputFileName;
            existing.OutputSizeBytes = voiceChanged ? null : existing.OutputSizeBytes;
            existing.DurationSeconds = voiceChanged ? null : existing.DurationSeconds;
            existing.CompletedAt = null;
            existing.StartedAt = null;
            existing.TotalChunks = voiceChanged ? 0 : existing.TotalChunks;
            existing.CompletedChunks = voiceChanged ? 0 : existing.CompletedChunks;
            audiobook = await audiobookRepository.UpdateAsync(existing);
        }

        string jobId = backgroundJobs.Enqueue<ITtsJobService>(s => s.GenerateAsync(bookId, CancellationToken.None));
        audiobook.HangfireJobId = jobId;
        audiobook = await audiobookRepository.UpdateAsync(audiobook);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Enqueued audiobook generation for book {BookId} (voice={Voice}) as Hangfire job {JobId}",
                bookId, request.VoiceName, jobId);
        }

        return Result.Success(MergeWithLiveProgress(audiobook));
    }

    public Task<Result<IReadOnlyList<KokoroVoiceDto>>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated())
        {
            return Task.FromResult(Result<IReadOnlyList<KokoroVoiceDto>>.Unauthorized());
        }

        if (!userContext.IsAdministrator())
        {
            return Task.FromResult(Result<IReadOnlyList<KokoroVoiceDto>>.Forbidden());
        }

        var voices = kokoroProvider.GetVoices();
        IReadOnlyList<KokoroVoiceDto> dtos = voices
            .Select(MapVoice)
            .OrderBy(v => v.Language, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(Result.Success(dtos));
    }

    public async Task<Result<string>> EnsureVoiceSampleAsync(string voiceName, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated())
        {
            return Result.Unauthorized();
        }

        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var voice = kokoroProvider.FindVoice(voiceName);
        if (voice is null)
        {
            return Result.NotFound();
        }

        string outputPath = storage.GetVoiceSamplePath(voice.Name);
        if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
        {
            return Result.Success(outputPath);
        }

        var engine = await kokoroProvider.GetEngineAsync(cancellationToken);

        try
        {
            float[] samples = await TtsJobService.SynthesiseChunkAsync(
                engine, SampleSentence, voice, voice.GetLangCode(), cancellationToken);
            await Tts.AudioStitcher.WriteWavAsync(samples, outputPath, cancellationToken);
            return Result.Success(outputPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to generate voice preview for '{Voice}'", voiceName);
            return Result.Error("Could not generate voice preview.");
        }
    }

    public async Task<Result> DeleteAsync(int bookId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated())
        {
            return Result.Unauthorized();
        }

        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var existing = await audiobookRepository.FindOneAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.BookId == bookId,
            CancellationToken = cancellationToken,
        });
        if (existing is null)
        {
            return Result.NotFound();
        }

        if (existing.Status is AudiobookStatus.Pending or AudiobookStatus.Running)
        {
            return Result.Conflict("Generation is still in progress — cancel it first, then you can start again.");
        }

        if (existing.Status != AudiobookStatus.Completed)
        {
            return Result.Conflict("Use Cancel to clear a failed generation.");
        }

        await audiobookRepository.DeleteAsync(existing);
        storage.DeleteAudiobook(bookId);
        progressTracker.Finish(bookId);

        return Result.Success();
    }

    public async Task<Result> CancelAsync(int bookId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated())
        {
            return Result.Unauthorized();
        }

        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var existing = await audiobookRepository.FindOneAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.BookId == bookId,
            CancellationToken = cancellationToken,
        });
        if (existing is null)
        {
            return Result.NotFound();
        }

        if (existing.Status is AudiobookStatus.Completed)
        {
            return Result.Conflict("Use Delete to remove a finished audiobook.");
        }

        if (!string.IsNullOrEmpty(existing.HangfireJobId))
        {
            try
            {
                bool removedFromQueue = BackgroundJob.Delete(existing.HangfireJobId);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Hangfire.Delete({JobId}) for book {BookId} -> {Removed}",
                        existing.HangfireJobId, bookId, removedFromQueue);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete Hangfire job {JobId} for book {BookId}", existing.HangfireJobId, bookId);
            }
        }

        progressTracker.Finish(bookId);
        storage.DeleteAudiobook(bookId);
        await audiobookRepository.DeleteAsync(existing);

        return Result.Success();
    }

    private AudiobookDto MergeWithLiveProgress(Audiobook a)
    {
        var live = progressTracker.GetSnapshot(a.BookId);

        int total = live?.TotalChunks ?? a.TotalChunks;
        int done = live?.CompletedChunks ?? a.CompletedChunks;
        double? percent = total > 0
            ? Math.Min(100d, Math.Round(100d * done / total, 1))
            : a.Status == AudiobookStatus.Completed ? 100d : null;

        return new AudiobookDto(
            a.BookId,
            (AudiobookState)(int)a.Status,
            a.VoiceName,
            total,
            done,
            percent,
            live?.CurrentStage,
            a.OutputSizeBytes,
            a.DurationSeconds,
            a.ErrorMessage,
            a.CreatedAt,
            a.StartedAt,
            a.CompletedAt);
    }

    private static KokoroVoiceDto MapVoice(KokoroVoice voice)
    {
        var language = voice.GetLanguage();
        char genderChar = voice.Name.Length > 1 ? voice.Name[1] : ' ';
        string gender = genderChar switch
        {
            'm' => "Male",
            'f' => "Female",
            _ => "Neutral",
        };

        // Voice names look like "af_heart" — the prefix is language/gender, suffix is the
        // speaker. Surface the human-readable speaker as a display name.
        string speaker = voice.Name.Length > 3 ? voice.Name[3..] : voice.Name;
        speaker = char.ToUpperInvariant(speaker[0]) + speaker[1..].Replace('_', ' ');

        return new KokoroVoiceDto(
            voice.Name,
            speaker,
            FormatLanguage(language),
            gender);
    }

    private static string FormatLanguage(KokoroLanguage language) => language switch
    {
        KokoroLanguage.AmericanEnglish => "American English",
        KokoroLanguage.BritishEnglish => "British English",
        KokoroLanguage.Japanese => "Japanese",
        KokoroLanguage.MandarinChinese => "Mandarin Chinese",
        KokoroLanguage.Spanish => "Spanish",
        KokoroLanguage.French => "French",
        KokoroLanguage.Hindi => "Hindi",
        KokoroLanguage.Italian => "Italian",
        KokoroLanguage.BrazilianPortuguese => "Brazilian Portuguese",
        _ => language.ToString(),
    };
}