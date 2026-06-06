using Shelfwarden.Models.Metadata;

namespace Shelfwarden.Services.Metadata;

/// <summary>
/// Aggregates every registered <see cref="IBookMetadataProvider"/>: fans a query out to all of them
/// in parallel, then ranks/merges the results (loosely mirroring Calibre's identify pipeline).
/// </summary>
public interface IBookMetadataService
{
    /// <summary>
    /// Returns ranked candidates from all enabled providers for the (admin-initiated) book-editor
    /// "Fetch metadata" flow. Admin-gated.
    /// </summary>
    Task<Result<IReadOnlyList<ExternalBookMetadataDto>>> SearchAsync(BookMetadataSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single best-effort merged candidate (top result, with its null fields back-filled
    /// from lower-ranked results across providers), or null when nothing matched. Used by the scanner
    /// to back-fill missing metadata during import — deliberately <em>not</em> admin-gated so it can
    /// run inside a background scan job.
    /// </summary>
    Task<ExternalBookMetadataDto?> FindBestMatchAsync(BookMetadataQuery query, CancellationToken cancellationToken = default);
}
