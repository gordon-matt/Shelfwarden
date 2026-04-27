namespace Shelfwarden.Models;

/// <summary>
/// Live state of a library's scan, combining the persisted <c>LastScannedAt</c> with
/// runtime Hangfire job state. <see cref="State"/> reflects the most "active" condition —
/// running takes priority over queued, queued over historical.
/// <para>
/// While a scan is running the <see cref="Progress"/> field is populated with live counters
/// (files seen, books added/updated/skipped) so the UI can show meaningful progress
/// instead of an indeterminate spinner.
/// </para>
/// </summary>
public sealed record ScanStatusDto(
    int LibraryId,
    ScanState State,
    DateTime? LastScannedAt,
    ScanProgressDto? Progress = null);

/// <summary>
/// Live counters reported by an in-flight scan. All values reflect work completed so far —
/// there's no "total files" estimate up front because the scanner streams the file tree.
/// </summary>
public sealed record ScanProgressDto(
    int FilesScanned,
    int BooksAdded,
    int BooksUpdated,
    int BooksRemoved,
    int Errors,
    string? CurrentFile,
    DateTime StartedAt);

public enum ScanState
{
    /// <summary>Never scanned, no scan in flight.</summary>
    Idle = 0,

    /// <summary>A scan finished successfully at <c>LastScannedAt</c> and nothing is in flight.</summary>
    Succeeded = 1,

    /// <summary>A scan job is enqueued but hasn't started yet.</summary>
    Queued = 2,

    /// <summary>A scan job is currently executing.</summary>
    Running = 3,
}
