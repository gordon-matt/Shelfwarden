namespace Shelfwarden.Models;

/// <summary>
/// Snapshot of an audiobook generation job. Returned to the UI both when the audiobook is
/// finished and while it is being generated, so the book detail page can poll a single
/// endpoint regardless of state.
/// </summary>
/// <param name="BookId">Book this generation belongs to.</param>
/// <param name="State">Where the job is. <see cref="AudiobookState.None"/> means there is no row.</param>
/// <param name="VoiceName">Canonical Kokoro voice id.</param>
/// <param name="TotalChunks">Chunks the plan will synthesize. Zero until that number is known.</param>
/// <param name="CompletedChunks">Chunks finished so far.</param>
/// <param name="PercentComplete">Rough completion, or <see langword="null"/> when it cannot be computed.</param>
/// <param name="CurrentStage">
/// Pipeline step while <paramref name="State"/> is pending or running, such as synthesising or stitching.
/// <see langword="null"/> when idle, completed, or failed, or when no live worker snapshot exists.
/// </param>
/// <param name="OutputSizeBytes">Encoded file size, or <see langword="null"/> before a file exists.</param>
/// <param name="DurationSeconds">Playback length, or <see langword="null"/> before it is known.</param>
/// <param name="ErrorMessage">Why generation failed, or <see langword="null"/>.</param>
/// <param name="CreatedAt">When the row was created, in UTC.</param>
/// <param name="StartedAt">When a worker picked it up, or <see langword="null"/>.</param>
/// <param name="CompletedAt">When encoding finished, or <see langword="null"/>.</param>
/// <param name="SplitByChapter"><see langword="true"/> when the audiobook is one file per chapter.</param>
/// <param name="Chapters">Chapter files in playback order. Empty for a single file, or while generation is still running. <see langword="null"/> when chapters were not loaded.</param>
public sealed record AudiobookDto(
    int BookId,
    AudiobookState State,
    string VoiceName,
    int TotalChunks,
    int CompletedChunks,
    double? PercentComplete,
    string? CurrentStage,
    long? OutputSizeBytes,
    double? DurationSeconds,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    bool SplitByChapter = false,
    IReadOnlyList<AudiobookChapterDto>? Chapters = null);

/// <summary>One generated chapter file of a split audiobook.</summary>
/// <param name="Index">Zero-based chapter index.</param>
/// <param name="Title">Chapter title.</param>
/// <param name="SizeBytes">File size, in bytes.</param>
/// <param name="DurationSeconds">Playback length.</param>
public sealed record AudiobookChapterDto(
    int Index,
    string Title,
    long SizeBytes,
    double DurationSeconds);

/// <summary>
/// Row in the admin "all audiobooks" view: an audiobook plus the book it belongs to. Used to
/// list every generation across the library so an administrator can play, cancel or delete them.
/// </summary>
/// <param name="BookId">Book this generation belongs to.</param>
/// <param name="BookTitle">That book's title.</param>
/// <param name="CoverImagePath">Relative cover path, or <see langword="null"/>.</param>
/// <param name="AuthorNames">Authors, or <see langword="null"/> when the book has none.</param>
/// <param name="State">Where the job is.</param>
/// <param name="VoiceName">Canonical Kokoro voice id.</param>
/// <param name="PercentComplete">Rough completion, or <see langword="null"/>.</param>
/// <param name="CurrentStage">Pipeline step, or <see langword="null"/> when no worker is reporting one.</param>
/// <param name="OutputSizeBytes">Encoded file size, or <see langword="null"/>.</param>
/// <param name="DurationSeconds">Playback length, or <see langword="null"/>.</param>
/// <param name="ErrorMessage">Why generation failed, or <see langword="null"/>.</param>
/// <param name="CreatedAt">When the row was created, in UTC.</param>
/// <param name="StartedAt">When a worker picked it up, or <see langword="null"/>.</param>
/// <param name="CompletedAt">When encoding finished, or <see langword="null"/>.</param>
/// <param name="SplitByChapter"><see langword="true"/> when the audiobook is one file per chapter.</param>
/// <param name="ChapterCount">Chapter files. Zero for a single-file audiobook.</param>
public sealed record AudiobookSummaryDto(
    int BookId,
    string BookTitle,
    string? CoverImagePath,
    string? AuthorNames,
    AudiobookState State,
    string VoiceName,
    double? PercentComplete,
    string? CurrentStage,
    long? OutputSizeBytes,
    double? DurationSeconds,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    bool SplitByChapter,
    int ChapterCount);

/// <summary>
/// Same values as the stored audiobook status, redeclared here so this project does not reference the data project.
/// <see cref="None"/> means there is no row.
/// </summary>
public enum AudiobookState
{
    /// <summary>No row exists yet. The user has not requested generation for this book.</summary>
    None = -1,

    /// <summary>Queued, and not yet started.</summary>
    Pending = 0,

    /// <summary>The worker is generating the audiobook.</summary>
    Running = 1,

    /// <summary>The encoded file is on disk.</summary>
    Completed = 2,

    /// <summary>Generation failed. <see cref="AudiobookDto.ErrorMessage"/> has the reason when one was stored.</summary>
    Failed = 3,
}

/// <summary>One Kokoro voice the user can pick from when starting generation.</summary>
/// <param name="Name">Canonical id, such as <c>af_heart</c>. This is what <see cref="GenerateAudiobookRequest.VoiceName"/> stores.</param>
/// <param name="DisplayName">Name shown in the picker.</param>
/// <param name="Language">Language of the voice.</param>
/// <param name="Gender">Voice gender, as the engine reports it.</param>
public sealed record KokoroVoiceDto(
    string Name,
    string DisplayName,
    string Language,
    string Gender);

/// <summary>Options the user picked for an audiobook generation request.</summary>
public sealed record GenerateAudiobookRequest
{
    /// <summary>Canonical voice id, such as <see cref="KokoroVoiceDto.Name"/>, not the display name.</summary>
    [Required]
    [StringLength(64)]
    public required string VoiceName { get; init; }

    /// <summary>When true, generate one audio file per chapter (driven by section boundaries).</summary>
    public bool SplitByChapter { get; init; }

    /// <summary>
    /// The reviewed sections to synthesise (only <see cref="BookSection.IsIncluded"/> ones are
    /// read). <see langword="null"/> means the whole book, which is the older whole-book generation.
    /// </summary>
    public IReadOnlyList<BookSection>? Sections { get; init; }
}