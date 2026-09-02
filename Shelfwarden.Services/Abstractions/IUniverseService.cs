namespace Shelfwarden.Services;

/// <summary>
/// Fictional universes: a way of grouping related <see cref="Series"/> and <see cref="Book"/>s,
/// with universe-specific timeline information and reading orders.
/// <para>
/// Universes are catalog data, so reads are open to any signed-in user while every mutation is
/// administrator-only — the same split <see cref="ISeriesService"/> uses.
/// </para>
/// <para>
/// Series membership and book membership are deliberately independent: assigning a series to a
/// universe does not implicitly pull its books onto the timeline, and a book can sit on a
/// timeline whose series belongs to no universe at all. <see cref="UniverseBook"/> is the
/// authoritative answer to "is this book in this universe".
/// </para>
/// </summary>
public interface IUniverseService
{
    /// <param name="query">Optional case-insensitive name filter.</param>
    Task<Result<IReadOnlyList<UniverseDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default);

    /// <summary>Lightweight lookup for the universe dropdowns on the book / series edit forms.</summary>
    Task<Result<IReadOnlyList<UniverseOptionDto>>> SearchAsync(string? query = null, int limit = 100, CancellationToken cancellationToken = default);

    Task<Result<UniverseDetailDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<UniverseDto>> CreateAsync(CreateUniverseRequest request, CancellationToken cancellationToken = default);

    Task<Result<UniverseDto>> UpdateAsync(int id, UpdateUniverseRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the universe. Books and series survive: timeline rows and universe reading orders
    /// are removed, and any series pointing at the universe has its assignment cleared.
    /// </summary>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends books to the universe timeline. Books already in the universe are skipped, so
    /// repeated calls can't produce duplicate <see cref="UniverseBook"/> rows.
    /// </summary>
    /// <returns>Number of books actually added.</returns>
    Task<Result<int>> AddBooksAsync(int universeId, IReadOnlyCollection<int> bookIds, CancellationToken cancellationToken = default);

    /// <summary>Removes the membership row only — the book itself is untouched.</summary>
    Task<Result> RemoveBookAsync(int universeId, int bookId, CancellationToken cancellationToken = default);

    /// <summary>Sets the descriptive in-universe date. Never affects ordering.</summary>
    Task<Result> SetTimelineDateAsync(int universeId, int bookId, string? timelineDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-applies <see cref="UniverseBook.TimelineOrder"/> so it matches the supplied id order
    /// (UniverseBook id → new order). Ids not in the universe are ignored.
    /// </summary>
    Task<Result> ReorderTimelineAsync(int universeId, IReadOnlyList<int> orderedUniverseBookIds, CancellationToken cancellationToken = default);

    /// <summary>Reads the universe a book belongs to, or null when it isn't in one.</summary>
    Task<Result<UniverseMembershipDto?>> GetBookMembershipAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a book in a universe (or moves / removes it when <paramref name="universeId"/> changes
    /// or is null). Used by the book edit page, which only ever deals with one membership.
    /// </summary>
    Task<Result> SetBookMembershipAsync(int bookId, int? universeId, string? timelineDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns (or clears, when <paramref name="universeId"/> is null) the series' universe.
    /// </summary>
    /// <param name="addSeriesBooks">
    /// When true, every book currently in the series is also added to the universe timeline.
    /// Off by default because the two relationships are not kept in sync afterwards.
    /// </param>
    Task<Result> SetSeriesUniverseAsync(int seriesId, int? universeId, bool addSeriesBooks = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a reading order owned by the universe. It's a normal <see cref="ReadingList"/>
    /// stamped with the universe id, so it's hidden from the regular reading lists page but
    /// reuses all the existing ordering / add-book behaviour.
    /// </summary>
    Task<Result<UniverseReadingListDto>> CreateReadingListAsync(
        int universeId,
        CreateReadingListRequest request,
        CancellationToken cancellationToken = default);
}
