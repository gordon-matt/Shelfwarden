namespace Shelfwarden.Services;

/// <summary>
/// Per-user, per-book state that isn't a reading position: the calling user's own star rating and
/// their free-form notes about the book. Backed by the <c>BookUsers</c> table (keyed on BookId + UserId).
/// </summary>
public interface IBookUserService
{
    /// <summary>Returns the calling user's rating + notes for the book, with nulls when nothing is saved yet.</summary>
    Task<Result<BookUserDto>> GetAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets (or clears) the calling user's star rating for the book. Pass 0 or null to un-rate.
    /// Returns the effective rating.
    /// </summary>
    Task<Result<int?>> SetRatingAsync(int bookId, int? rating, CancellationToken cancellationToken = default);

    /// <summary>Sets (or clears) the calling user's free-form notes for the book.</summary>
    Task<Result> SetNotesAsync(int bookId, string? notes, CancellationToken cancellationToken = default);
}
