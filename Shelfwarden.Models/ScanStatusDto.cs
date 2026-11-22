namespace Shelfwarden.Models;

/// <summary>
/// Live state of a shelf's scan, combining the persisted <c>LastScannedAt</c> with
/// runtime Hangfire job state. <see cref="State"/> reflects the most "active" condition —
/// running takes priority over queued, queued over historical.
/// <para>
/// While a scan is running the <see cref="Progress"/> field is populated with live counters
/// (files seen, books added/updated/skipped) so the UI can show meaningful progress
/// instead of an indeterminate spinner.
/// </para>
/// </summary>
/// <param name="ShelfId">Shelf id.</param>
/// <param name="State">The most active condition: running, then queued, then a finished scan, then never scanned.</param>
/// <param name="LastScannedAt">When the last successful scan finished, or <see langword="null"/>.</param>
/// <param name="Progress">Live counters while a scan is running, or <see langword="null"/>.</param>
public sealed record ScanStatusDto(
    int ShelfId,
    ScanState State,
    DateTime? LastScannedAt,
    ScanProgressDto? Progress = null);

/// <summary>
/// Live counters reported by an in-flight scan. All values reflect work completed so far —
/// there's no "total files" estimate up front because the scanner streams the file tree.
/// </summary>
/// <param name="FilesScanned">Files visited so far.</param>
/// <param name="BooksAdded">Books added so far.</param>
/// <param name="BooksUpdated">Books updated so far.</param>
/// <param name="BooksRemoved">Books whose files were gone and whose rows were removed.</param>
/// <param name="Errors">Files that could not be read.</param>
/// <param name="CurrentFile">File being read, or <see langword="null"/> between files.</param>
/// <param name="StartedAt">When this scan started, in UTC.</param>
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