namespace Shelfwarden.Services;

/// <summary>
/// Cover-image operations for a book that live outside the normal <see cref="UpdateBookRequest"/>
/// flow (covers are stored as files, not columns). Both operations are administrator-gated and
/// applied immediately — the book edit page calls them when the user saves.
/// </summary>
public interface IBookCoverService
{
    /// <summary>
    /// Downloads <paramref name="imageUrl"/>, validates it is a real image, stores it as the book's
    /// cover and updates <c>CoverImagePath</c>. Used when the user picks an online cover in the
    /// "Fetch metadata" modal.
    /// </summary>
    Task<Result> SetCoverFromUrlAsync(int bookId, string imageUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-extracts the cover embedded in the book's own file and stores it, discarding any previously
    /// applied online cover. Returns an error when the file has no embedded cover.
    /// </summary>
    Task<Result> RevertCoverToEmbeddedAsync(int bookId, CancellationToken cancellationToken = default);
}
