using System.Text.Json;
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
    IEbookSectionParserFactory sectionParserFactory,
    IAudiobookProgressTracker progressTracker,
    IStoragePathProvider storage) : IAudiobookService
{
    private const string SampleSentence = "The quick brown fox jumps over the lazy dog.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

        return existing is null or { Status: AudiobookStatus.Deleted }
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
                CompletedAt: null,
                SplitByChapter: false,
                Chapters: null))
            : Result.Success(MergeWithLiveProgress(existing));
    }

    public async Task<Result<IReadOnlyList<AudiobookSummaryDto>>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated())
        {
            return Result.Unauthorized();
        }

        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var rows = await audiobookRepository.FindAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.Status != AudiobookStatus.Deleted,
            Include = query => query.Include(a => a.Book).ThenInclude(b => b.BookAuthors).ThenInclude(ba => ba.Author),
            SplitQuery = true,
            CancellationToken = cancellationToken,
        });

        IReadOnlyList<AudiobookSummaryDto> list = rows
            .Select(MapSummary)
            // Active work (running, then queued) first; finished/failed sorted by most recent.
            .OrderBy(s => s.State switch
            {
                AudiobookState.Running => 0,
                AudiobookState.Pending => 1,
                AudiobookState.Failed => 2,
                _ => 3,
            })
            .ThenByDescending(s => s.CreatedAt)
            .ToList();

        return Result.Success(list);
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

        string? sectionPlanJson = request.Sections is { Count: > 0 }
            ? JsonSerializer.Serialize(request.Sections, JsonOptions)
            : null;

        if (request.Sections is { Count: > 0 } && request.Sections.All(s => !s.IsIncluded))
        {
            return Result.Invalid(new ValidationError(nameof(request.Sections),
                "Select at least one section to include in the audiobook."));
        }

        // Any change to the voice OR the chapter plan invalidates the partial work on disk —
        // chunks made under the old settings can't be spliced into the new output.
        bool planChanged = existing is not null
            && (!string.Equals(existing.VoiceName, request.VoiceName, StringComparison.OrdinalIgnoreCase)
                || existing.SplitByChapter != request.SplitByChapter
                || !string.Equals(existing.SectionPlanJson, sectionPlanJson, StringComparison.Ordinal));
        if (planChanged)
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
                SplitByChapter = request.SplitByChapter,
                SectionPlanJson = sectionPlanJson,
                RequestedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Status = AudiobookStatus.Pending;
            existing.VoiceName = request.VoiceName;
            existing.SplitByChapter = request.SplitByChapter;
            existing.SectionPlanJson = sectionPlanJson;
            existing.RequestedByUserId = userId;
            existing.ErrorMessage = null;
            existing.OutputFileName = planChanged ? null : existing.OutputFileName;
            existing.OutputSizeBytes = planChanged ? null : existing.OutputSizeBytes;
            existing.DurationSeconds = planChanged ? null : existing.DurationSeconds;
            existing.ChaptersJson = planChanged ? null : existing.ChaptersJson;
            existing.CompletedAt = null;
            existing.StartedAt = null;
            existing.TotalChunks = planChanged ? 0 : existing.TotalChunks;
            existing.CompletedChunks = planChanged ? 0 : existing.CompletedChunks;
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

    public async Task<Result<AudiobookPlanDto>> GetSectionsAsync(int bookId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAuthenticated())
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

        if (!File.Exists(book.FilePath))
        {
            return Result.Error("The book file is missing on disk.");
        }

        var parser = sectionParserFactory.GetFor(book.FilePath);
        if (parser is null)
        {
            return Result.Invalid(new ValidationError(nameof(book.FileFormat),
                "Only EPUB and PDF books support text-to-speech."));
        }

        // The last generation's choices live on the audiobook row and survive a (soft) delete, so
        // pre-apply them even when there is no active audiobook right now.
        var lastAudiobook = await audiobookRepository.FindOneAsync(new SearchOptions<Audiobook>
        {
            Query = a => a.BookId == bookId,
            CancellationToken = cancellationToken,
        });

        try
        {
            var result = await parser.ParseSectionsAsync(book.FilePath, cancellationToken);
            var sections = ApplySavedSelection(result.Sections, lastAudiobook?.SectionPlanJson);
            return Result.Success(new AudiobookPlanDto(
                sections, result.Quality, result.Warning, lastAudiobook?.SplitByChapter ?? false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to parse sections for book {BookId}", bookId);
            return Result.Error("Could not analyse this book's chapters.");
        }
    }

    /// <summary>
    /// Overlays the user's previously saved include / chapter-boundary choices onto freshly detected
    /// sections, matched by section identity (title + page range / reading-order start). Sections that
    /// don't match a saved entry keep their detected defaults, so the merge degrades gracefully if the
    /// book's structure has changed since the last generation.
    /// </summary>
    private IReadOnlyList<BookSection> ApplySavedSelection(IReadOnlyList<BookSection> detected, string? savedPlanJson)
    {
        if (string.IsNullOrWhiteSpace(savedPlanJson))
        {
            return detected;
        }

        List<BookSection>? saved;
        try
        {
            saved = JsonSerializer.Deserialize<List<BookSection>>(savedPlanJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not read saved audiobook section plan; using detected defaults.");
            return detected;
        }

        if (saved is not { Count: > 0 })
        {
            return detected;
        }

        var savedByKey = new Dictionary<string, BookSection>();
        foreach (var s in saved)
        {
            savedByKey[SectionKey(s)] = s;
        }

        foreach (var section in detected)
        {
            if (savedByKey.TryGetValue(SectionKey(section), out var match))
            {
                section.IsIncluded = match.IsIncluded;
                section.IsChapterBoundary = match.IsChapterBoundary;
            }
        }

        return detected;
    }

    private static string SectionKey(BookSection s)
    {
        string locator = s.StartPage is int start
            ? $"p{start}-{s.EndPage}"
            : $"r{(s.ReadingOrderIndices.Count > 0 ? s.ReadingOrderIndices[0] : -1)}";
        return $"{s.Title}|{locator}";
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

        await SoftDeleteAsync(existing);

        return Result.Success();
    }

    /// <summary>
    /// Removes the generated output from disk and marks the row <see cref="AudiobookStatus.Deleted"/>,
    /// keeping the voice / chapter-split / section-plan choices so they can be pre-applied next time.
    /// </summary>
    private async Task SoftDeleteAsync(Audiobook existing)
    {
        storage.DeleteAudiobook(existing.BookId);
        progressTracker.Finish(existing.BookId);

        existing.Status = AudiobookStatus.Deleted;
        existing.OutputFileName = null;
        existing.ChaptersJson = null;
        existing.OutputSizeBytes = null;
        existing.DurationSeconds = null;
        existing.ErrorMessage = null;
        existing.HangfireJobId = null;
        existing.TotalChunks = 0;
        existing.CompletedChunks = 0;
        existing.StartedAt = null;
        existing.CompletedAt = null;

        await audiobookRepository.UpdateAsync(existing);
    }

    public async Task<Result<AudiobookDto>> DeleteChapterAsync(
        int bookId,
        int chapterIndex,
        CancellationToken cancellationToken = default)
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

        if (existing.Status != AudiobookStatus.Completed || !existing.SplitByChapter)
        {
            return Result.Conflict("Only individual chapters of a completed split audiobook can be removed.");
        }

        var chapters = DeserializeChapters(existing.ChaptersJson) ?? [];
        if (chapters.Count == 0 || chapters.All(c => c.Index != chapterIndex))
        {
            return Result.NotFound();
        }

        string chapterPath = storage.GetAudiobookChapterFilePath(bookId, chapterIndex);
        try
        {
            if (File.Exists(chapterPath))
            {
                File.Delete(chapterPath);
            }

            string workingDir = storage.GetAudiobookChapterWorkingDirectory(bookId, chapterIndex);
            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete chapter file for book {BookId} chapter {Index}", bookId, chapterIndex);
            return Result.Error("Could not delete the chapter file from disk.");
        }

        var remaining = chapters.Where(c => c.Index != chapterIndex).ToList();
        if (remaining.Count == 0)
        {
            await SoftDeleteAsync(existing);

            return Result.Success(new AudiobookDto(
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
                CompletedAt: null,
                SplitByChapter: false,
                Chapters: null));
        }

        existing.ChaptersJson = JsonSerializer.Serialize(remaining, JsonOptions);
        existing.OutputSizeBytes = remaining.Sum(c => c.SizeBytes);
        existing.DurationSeconds = remaining.Sum(c => c.DurationSeconds);
        existing = await audiobookRepository.UpdateAsync(existing);

        return Result.Success(MergeWithLiveProgress(existing));
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

        await SoftDeleteAsync(existing);

        return Result.Success();
    }

    private AudiobookSummaryDto MapSummary(Audiobook a)
    {
        var live = progressTracker.GetSnapshot(a.BookId);

        int total = live?.TotalChunks ?? a.TotalChunks;
        int done = live?.CompletedChunks ?? a.CompletedChunks;
        double? percent = total > 0
            ? Math.Min(100d, Math.Round(100d * done / total, 1))
            : a.Status == AudiobookStatus.Completed ? 100d : null;

        string? authorNames = a.Book?.BookAuthors is { Count: > 0 } authors
            ? string.Join(", ", authors
                .Select(ba => ba.Author?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n)))
            : null;

        return new AudiobookSummaryDto(
            a.BookId,
            a.Book?.Title ?? $"Book #{a.BookId}",
            a.Book?.CoverImagePath,
            string.IsNullOrWhiteSpace(authorNames) ? null : authorNames,
            (AudiobookState)(int)a.Status,
            a.VoiceName,
            percent,
            live?.CurrentStage,
            a.OutputSizeBytes,
            a.DurationSeconds,
            a.ErrorMessage,
            a.CreatedAt,
            a.StartedAt,
            a.CompletedAt,
            a.SplitByChapter,
            DeserializeChapters(a.ChaptersJson)?.Count ?? 0);
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
            a.CompletedAt,
            a.SplitByChapter,
            DeserializeChapters(a.ChaptersJson));
    }

    private static IReadOnlyList<AudiobookChapterDto>? DeserializeChapters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<AudiobookChapterDto>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
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