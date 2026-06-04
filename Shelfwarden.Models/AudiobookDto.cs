namespace Shelfwarden.Models;

/// <summary>
/// Snapshot of an audiobook generation job. Returned to the UI both when the audiobook is
/// finished and while it is being generated, so the book detail page can poll a single
/// endpoint regardless of state.
/// </summary>
public sealed record AudiobookDto(
    int BookId,
    AudiobookState State,
    string VoiceName,
    int TotalChunks,
    int CompletedChunks,
    double? PercentComplete,
    /// <summary>
    /// Human-readable pipeline step while <see cref="State"/> is pending or running (e.g.
    /// synthesising vs stitching). Null when idle / completed / failed or when no live worker
    /// snapshot exists.
    /// </summary>
    string? CurrentStage,
    long? OutputSizeBytes,
    double? DurationSeconds,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    /// <summary>True when the audiobook is split into one file per chapter rather than a single file.</summary>
    bool SplitByChapter = false,
    /// <summary>
    /// The generated chapter files, in playback order. Empty for a single-file audiobook or while
    /// generation is still in progress.
    /// </summary>
    IReadOnlyList<AudiobookChapterDto>? Chapters = null);

/// <summary>One generated chapter file of a split audiobook.</summary>
public sealed record AudiobookChapterDto(
    int Index,
    string Title,
    long SizeBytes,
    double DurationSeconds);

/// <summary>
/// Row in the admin "all audiobooks" view: an audiobook plus the book it belongs to. Used to
/// list every generation across the library so an administrator can play, cancel or delete them.
/// </summary>
public sealed record AudiobookSummaryDto(
    int BookId,
    string BookTitle,
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
/// Mirrors <c>Shelfwarden.Data.Entities.AudiobookStatus</c>; we redeclare it on the model side
/// so the Models project never needs to take a dependency on the Data project.
/// </summary>
public enum AudiobookState
{
    /// <summary>No row exists yet — the user hasn't requested generation for this book.</summary>
    None = -1,

    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>One Kokoro voice the user can pick from when starting generation.</summary>
public sealed record KokoroVoiceDto(
    string Name,
    string DisplayName,
    string Language,
    string Gender);

/// <summary>Options the user picked for an audiobook generation request.</summary>
public sealed record GenerateAudiobookRequest
{
    [Required]
    [StringLength(64)]
    public required string VoiceName { get; init; }

    /// <summary>When true, generate one audio file per chapter (driven by section boundaries).</summary>
    public bool SplitByChapter { get; init; }

    /// <summary>
    /// The reviewed sections to synthesise (only <see cref="BookSection.IsIncluded"/> ones are
    /// read). Null means "the whole book" — used for backward-compatible whole-book generation.
    /// </summary>
    public IReadOnlyList<BookSection>? Sections { get; init; }
}