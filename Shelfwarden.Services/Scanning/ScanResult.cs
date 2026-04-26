namespace Shelfwarden.Services.Scanning;

/// <summary>
/// Aggregated counters returned at the end of a library scan. Surfaced through the API so the
/// UI can show "Scan finished: 42 added, 3 updated, 1 removed".
/// </summary>
public sealed record ScanResult
{
    public int FilesScanned { get; init; }

    public int BooksAdded { get; init; }

    public int BooksUpdated { get; init; }

    public int BooksRemoved { get; init; }

    public int Errors { get; init; }

    public TimeSpan Duration { get; init; }

    public static ScanResult Empty => new();
}
