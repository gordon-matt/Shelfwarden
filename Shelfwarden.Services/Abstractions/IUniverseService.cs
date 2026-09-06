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
    /// Adds books to the universe. They arrive unscheduled — assigning them a
    /// <see cref="TimelineDate"/> is a separate, deliberate step. Books already in the universe
    /// are skipped, so repeated calls can't produce duplicate <see cref="UniverseBook"/> rows.
    /// </summary>
    /// <returns>Number of books actually added.</returns>
    Task<Result<int>> AddBooksAsync(int universeId, IReadOnlyCollection<int> bookIds, CancellationToken cancellationToken = default);

    /// <summary>Removes the membership row only — the book itself is untouched.</summary>
    Task<Result> RemoveBookAsync(int universeId, int bookId, CancellationToken cancellationToken = default);

    /// <summary>The universe's timeline dates, in timeline order. Feeds the date dropdowns.</summary>
    Task<Result<IReadOnlyList<UniverseTimelineDateDto>>> ListTimelineDatesAsync(int universeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a date to the end of the universe's timeline, and syncs the universe's
    /// <see cref="TimelineType"/> to match what was supplied. Re-using a name that already exists
    /// in the universe is rejected, since two identical dates would be indistinguishable in every
    /// dropdown — that check only applies when <paramref name="name"/> is given explicitly.
    /// </summary>
    /// <param name="name">
    /// Free-text label, never parsed. Leave <c>null</c>/blank when the universe uses
    /// <see cref="TimelineType.Numeric"/> dates — those store no name and are labelled from
    /// <paramref name="yearFrom"/>/<paramref name="yearTo"/> at display time. At least one of
    /// <paramref name="name"/> or a year is required.
    /// </param>
    /// <param name="yearFrom">
    /// Optional numeric start year. Supplying either year switches the universe to
    /// <see cref="TimelineType.Numeric"/>; leaving both null (with <paramref name="name"/> set)
    /// switches it back to <see cref="TimelineType.Named"/>.
    /// </param>
    /// <param name="yearTo">Optional numeric end year. Defaults to <paramref name="yearFrom"/> for a point in time.</param>
    Task<Result<UniverseTimelineDateDto>> CreateTimelineDateAsync(
        int universeId,
        string? name,
        int? yearFrom = null,
        int? yearTo = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a timeline date (and/or changes its numeric years), syncing the owning universe's
    /// <see cref="TimelineType"/> the same way <see cref="CreateTimelineDateAsync"/> does. Every
    /// book on it follows automatically.
    /// </summary>
    Task<Result<UniverseTimelineDateDto>> RenameTimelineDateAsync(
        int timelineDateId,
        string? name,
        int? yearFrom = null,
        int? yearTo = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a timeline date. Books sitting on it aren't removed from the universe — they go
    /// back to being unscheduled.
    /// </summary>
    Task<Result> DeleteTimelineDateAsync(int timelineDateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-applies timeline order to match the supplied <see cref="TimelineDate"/> id order. Ids
    /// not belonging to the universe are ignored; dates the caller left out keep their relative
    /// order at the end.
    /// </summary>
    Task<Result> ReorderTimelineDatesAsync(int universeId, IReadOnlyList<int> orderedTimelineDateIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a book onto a timeline date, or back to unscheduled when
    /// <paramref name="timelineDateId"/> is null. It lands at the end of its new date's books.
    /// </summary>
    Task<Result> SetBookTimelineDateAsync(int universeId, int bookId, int? timelineDateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reorders the books sharing one timeline date (or the unscheduled bucket, when
    /// <paramref name="timelineDateId"/> is null) to match the supplied
    /// <see cref="UniverseBook"/> id order. Ids on any other date are ignored.
    /// </summary>
    Task<Result> ReorderTimelineGroupAsync(
        int universeId,
        int? timelineDateId,
        IReadOnlyList<int> orderedUniverseBookIds,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the universe a book belongs to, or null when it isn't in one.</summary>
    Task<Result<UniverseMembershipDto?>> GetBookMembershipAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a book in a universe (or moves / removes it when <paramref name="universeId"/> changes
    /// or is null). Used by the book edit page, which only ever deals with one membership.
    /// </summary>
    Task<Result> SetBookMembershipAsync(int bookId, int? universeId, int? timelineDateId, CancellationToken cancellationToken = default);

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
