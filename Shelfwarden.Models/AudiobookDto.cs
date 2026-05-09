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
    DateTime? CompletedAt);

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

/// <summary>Voice the user picked for an audiobook generation request.</summary>
public sealed record GenerateAudiobookRequest
{
    [Required]
    [StringLength(64)]
    public required string VoiceName { get; init; }
}