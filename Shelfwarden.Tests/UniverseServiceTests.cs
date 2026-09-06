using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shelfwarden.Models;
using Shelfwarden.Services;
using Shelfwarden.Services.Auth;
using Shelfwarden.Tests.Infrastructure;

namespace Shelfwarden.Tests;

/// <summary>
/// Covers the rules that make universes different from collections / reading lists: the timeline
/// is a set of hand-ordered <see cref="TimelineDate"/>s that books are explicitly assigned to (so
/// order never comes from the date text), books join unscheduled and are never evicted by date
/// changes, book and series membership never sync themselves, and deleting a universe must not
/// take any books or series with it.
/// </summary>
public class UniverseServiceTests : IClassFixture<TestDbFixture>
{
    private readonly TestDbFixture _fixture;

    public UniverseServiceTests(TestDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Timeline_columns_follow_the_dates_own_order_not_their_text()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Ordering Test" })).Value.Id;

        // Dates whose text sorts the "wrong" way round on purpose — order must ignore it entirely.
        int early = (await service.CreateTimelineDateAsync(universeId, "10,000 BC")).Value.Id;
        int late = (await service.CreateTimelineDateAsync(universeId, "500 BC")).Value.Id;

        int earlyBook = await SeedBookAsync(scope, shelfId, "Ten Thousand BC");
        int lateBook = await SeedBookAsync(scope, shelfId, "Five Hundred BC");
        await service.AddBooksAsync(universeId, [earlyBook, lateBook]);
        await service.SetBookTimelineDateAsync(universeId, earlyBook, early);
        await service.SetBookTimelineDateAsync(universeId, lateBook, late);

        var detail = (await service.GetByIdAsync(universeId)).Value;
        Assert.Equal(["10,000 BC", "500 BC"], detail.Timeline.Groups.Select(g => g.Label));

        await service.ReorderTimelineDatesAsync(universeId, [late, early]);

        detail = (await service.GetByIdAsync(universeId)).Value;
        Assert.Equal(["500 BC", "10,000 BC"], detail.Timeline.Groups.Select(g => g.Label));
        Assert.Equal([0, 1], detail.Timeline.Dates.Select(d => d.Order));
    }

    [Fact]
    public async Task Books_join_a_universe_unscheduled()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        var dates = scope.ServiceProvider.GetRequiredService<IRepository<TimelineDate>>();
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Unscheduled" })).Value.Id;
        int first = await SeedBookAsync(scope, shelfId, "Arrives First");
        int second = await SeedBookAsync(scope, shelfId, "Arrives Second");
        await service.AddBooksAsync(universeId, [first, second]);

        // Adding books must never invent dates — that's an explicit decision.
        Assert.Equal(0, await dates.CountAsync(td => td.UniverseId == universeId));

        var detail = (await service.GetByIdAsync(universeId)).Value;
        var group = Assert.Single(detail.Timeline.Groups);
        Assert.Null(group.TimelineDateId);
        Assert.Equal("Unscheduled", group.Label);
        Assert.Equal(["Arrives First", "Arrives Second"], group.Entries.Select(e => e.Book.Title));
        Assert.Equal([0, 1], group.Entries.Select(e => e.Order));
    }

    [Fact]
    public async Task Books_assigned_to_the_same_date_share_its_column_and_order_within_it()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Shared Date" })).Value.Id;
        int moment = (await service.CreateTimelineDateAsync(universeId, "Same Moment")).Value.Id;

        int a = await SeedBookAsync(scope, shelfId, "Book A");
        int b = await SeedBookAsync(scope, shelfId, "Book B");
        int c = await SeedBookAsync(scope, shelfId, "Book C");
        await service.AddBooksAsync(universeId, [a, b, c]);
        await service.SetBookTimelineDateAsync(universeId, a, moment);
        await service.SetBookTimelineDateAsync(universeId, b, moment);

        var detail = (await service.GetByIdAsync(universeId)).Value;
        var shared = detail.Timeline.Groups.Single(g => g.TimelineDateId == moment);
        Assert.Equal(["Book A", "Book B"], shared.Entries.Select(e => e.Book.Title));
        Assert.Equal([0, 1], shared.Entries.Select(e => e.Order));

        // Book C never got a date, so it stays in the trailing unscheduled column.
        var unscheduled = detail.Timeline.Groups.Single(g => g.TimelineDateId is null);
        Assert.Equal(["Book C"], unscheduled.Entries.Select(e => e.Book.Title));

        // Reordering one date's books leaves every other column alone.
        await service.ReorderTimelineGroupAsync(universeId, moment, shared.Entries.Select(e => e.Id).Reverse().ToList());

        detail = (await service.GetByIdAsync(universeId)).Value;
        shared = detail.Timeline.Groups.Single(g => g.TimelineDateId == moment);
        Assert.Equal(["Book B", "Book A"], shared.Entries.Select(e => e.Book.Title));
        Assert.Equal([0, 1], shared.Entries.Select(e => e.Order));
    }

    [Fact]
    public async Task Moving_a_book_to_another_date_closes_the_gap_it_left()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Moving" })).Value.Id;
        int before = (await service.CreateTimelineDateAsync(universeId, "Before")).Value.Id;
        int after = (await service.CreateTimelineDateAsync(universeId, "After")).Value.Id;

        int a = await SeedBookAsync(scope, shelfId, "Stays");
        int b = await SeedBookAsync(scope, shelfId, "Moves");
        int c = await SeedBookAsync(scope, shelfId, "Also Stays");
        await service.AddBooksAsync(universeId, [a, b, c]);
        foreach (int bookId in new[] { a, b, c })
        {
            await service.SetBookTimelineDateAsync(universeId, bookId, before);
        }

        await service.SetBookTimelineDateAsync(universeId, b, after);

        var detail = (await service.GetByIdAsync(universeId)).Value;
        var beforeGroup = detail.Timeline.Groups.Single(g => g.TimelineDateId == before);
        Assert.Equal(["Stays", "Also Stays"], beforeGroup.Entries.Select(e => e.Book.Title));
        Assert.Equal([0, 1], beforeGroup.Entries.Select(e => e.Order));

        var afterGroup = detail.Timeline.Groups.Single(g => g.TimelineDateId == after);
        Assert.Equal(["Moves"], afterGroup.Entries.Select(e => e.Book.Title));
    }

    [Fact]
    public async Task Deleting_a_date_unschedules_its_books_rather_than_evicting_them()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Doomed Date" })).Value.Id;
        int doomed = (await service.CreateTimelineDateAsync(universeId, "Doomed")).Value.Id;
        int keeper = (await service.CreateTimelineDateAsync(universeId, "Keeper")).Value.Id;

        int bookId = await SeedBookAsync(scope, shelfId, "Orphan");
        await service.AddBooksAsync(universeId, [bookId]);
        await service.SetBookTimelineDateAsync(universeId, bookId, doomed);

        Assert.True((await service.DeleteTimelineDateAsync(doomed)).IsSuccess);

        var detail = (await service.GetByIdAsync(universeId)).Value;
        var entry = Assert.Single(detail.Timeline.Entries);
        Assert.Equal("Orphan", entry.Book.Title);
        Assert.Null(entry.TimelineDateId);

        // The surviving date closes the gap the deleted one left behind.
        var remaining = Assert.Single(detail.Timeline.Dates);
        Assert.Equal(keeper, remaining.Id);
        Assert.Equal(0, remaining.Order);
    }

    [Fact]
    public async Task Numeric_years_override_hand_ordering_and_handle_negative_eras()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Numeric Ordering" })).Value.Id;

        // Created in an order that would be wrong once numeric years take over, and one date (Order
        // 0) never gets a year at all — it should trail behind every numeric date once any exist.
        int textOnly = (await service.CreateTimelineDateAsync(universeId, "Sometime Later")).Value.Id;
        int recent = (await service.CreateTimelineDateAsync(universeId, "35 AD", yearFrom: 35, yearTo: 35)).Value.Id;
        int ancient = (await service.CreateTimelineDateAsync(universeId, "500 BC", yearFrom: -500, yearTo: -450)).Value.Id;

        var detail = (await service.GetByIdAsync(universeId)).Value;

        // -500 sorts before 35 (plain integer comparison), and the text-only date — which never got
        // a year — trails behind both even though it was created first.
        Assert.Equal([ancient, recent, textOnly], detail.Timeline.Dates.Select(d => d.Id));
        Assert.Equal([-500, 35, null], detail.Timeline.Dates.Select(d => d.YearFrom));
    }

    [Fact]
    public async Task A_date_without_a_numeric_year_is_rejected_when_from_is_after_to()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Backwards Range" })).Value.Id;

        var result = await service.CreateTimelineDateAsync(universeId, "Impossible", yearFrom: 100, yearTo: 50);

        Assert.False(result.IsSuccess);
        Assert.Equal(Ardalis.Result.ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Lanes_order_by_their_earliest_numeric_year_once_the_universe_has_one()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Lane Ordering" })).Value.Id;
        int earlyDate = (await service.CreateTimelineDateAsync(universeId, "Early", yearFrom: 100, yearTo: 100)).Value.Id;
        int lateDate = (await service.CreateTimelineDateAsync(universeId, "Late", yearFrom: 900, yearTo: 900)).Value.Id;

        int laterSeriesId = await SeedSeriesAsync(scope, "Zebra Series");
        int earlierSeriesId = await SeedSeriesAsync(scope, "Aardvark Series");
        int laterBook = await SeedBookAsync(scope, shelfId, "Later Book", laterSeriesId);
        int earlierBook = await SeedBookAsync(scope, shelfId, "Earlier Book", earlierSeriesId);
        await service.AddBooksAsync(universeId, [laterBook, earlierBook]);

        // Deliberately assign the alphabetically-later series to the earlier date, so a name-first
        // sort would get this backwards.
        await service.SetBookTimelineDateAsync(universeId, laterBook, earlyDate);
        await service.SetBookTimelineDateAsync(universeId, earlierBook, lateDate);

        var detail = (await service.GetByIdAsync(universeId)).Value;

        Assert.Equal(["Zebra Series", "Aardvark Series"], detail.Timeline.Rows.Select(r => r.Label));
    }

    [Fact]
    public async Task A_universe_starts_named_and_switches_to_numeric_once_a_year_is_given()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Type Switching" })).Value.Id;
        Assert.Equal(TimelineType.Named, (await service.GetByIdAsync(universeId)).Value.TimelineType);

        await service.CreateTimelineDateAsync(universeId, date: null, yearFrom: 1998);
        Assert.Equal(TimelineType.Numeric, (await service.GetByIdAsync(universeId)).Value.TimelineType);

        // Adding a plain-text date afterwards is a deliberate switch back — same as picking the
        // "Named" radio on the modal.
        await service.CreateTimelineDateAsync(universeId, date: "Some Chapter");
        Assert.Equal(TimelineType.Named, (await service.GetByIdAsync(universeId)).Value.TimelineType);
    }

    [Fact]
    public async Task A_numeric_only_date_gets_an_auto_derived_label()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Auto Label" })).Value.Id;

        var point = await service.CreateTimelineDateAsync(universeId, date: null, yearFrom: 1998);
        Assert.Equal("1998", point.Value.Date);

        var range = await service.CreateTimelineDateAsync(universeId, date: null, yearFrom: 2005, yearTo: 2008);
        Assert.Equal("2005-2008", range.Value.Date);
    }

    [Fact]
    public async Task Creating_a_date_without_text_or_a_year_is_rejected()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Nothing Given" })).Value.Id;

        var result = await service.CreateTimelineDateAsync(universeId, date: null);

        Assert.False(result.IsSuccess);
        Assert.Equal(Ardalis.Result.ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task A_lanes_overlapping_numeric_dates_merge_into_one_labelled_segment()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Merging Segments" })).Value.Id;
        int seriesId = await SeedSeriesAsync(scope, "Overlapping Series");

        // 2005-2008 and 2007-2012 overlap (2007 <= 2008), so on the same lane they must render as
        // one 2005-2012 bar rather than two overlapping ones.
        int first = (await service.CreateTimelineDateAsync(universeId, date: null, yearFrom: 2005, yearTo: 2008)).Value.Id;
        int second = (await service.CreateTimelineDateAsync(universeId, date: null, yearFrom: 2007, yearTo: 2012)).Value.Id;

        int bookA = await SeedBookAsync(scope, shelfId, "Book A", seriesId);
        int bookB = await SeedBookAsync(scope, shelfId, "Book B", seriesId);
        await service.AddBooksAsync(universeId, [bookA, bookB]);
        await service.SetBookTimelineDateAsync(universeId, bookA, first);
        await service.SetBookTimelineDateAsync(universeId, bookB, second);

        var detail = (await service.GetByIdAsync(universeId)).Value;
        var row = detail.Timeline.Rows.Single(r => r.SeriesId == seriesId);

        var segment = Assert.Single(row.Segments);
        Assert.Equal("2005-2012", segment.Label);
        Assert.Equal(2005, segment.YearFrom);
        Assert.Equal(2012, segment.YearTo);
        Assert.Equal(["Book A", "Book B"], segment.Entries.Select(e => e.Book.Title));
    }

    [Fact]
    public async Task A_lanes_non_overlapping_numeric_dates_stay_as_separate_segments()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Separate Segments" })).Value.Id;
        int seriesId = await SeedSeriesAsync(scope, "Gapped Series");

        // A genuine gap between 1999 and 2005 — these must stay two separate bars.
        int first = (await service.CreateTimelineDateAsync(universeId, date: null, yearFrom: 1998, yearTo: 1999)).Value.Id;
        int second = (await service.CreateTimelineDateAsync(universeId, date: null, yearFrom: 2005, yearTo: 2008)).Value.Id;

        int bookA = await SeedBookAsync(scope, shelfId, "Book A", seriesId);
        int bookB = await SeedBookAsync(scope, shelfId, "Book B", seriesId);
        await service.AddBooksAsync(universeId, [bookA, bookB]);
        await service.SetBookTimelineDateAsync(universeId, bookA, first);
        await service.SetBookTimelineDateAsync(universeId, bookB, second);

        var detail = (await service.GetByIdAsync(universeId)).Value;
        var row = detail.Timeline.Rows.Single(r => r.SeriesId == seriesId);

        Assert.Equal(["1998-1999", "2005-2008"], row.Segments.Select(s => s.Label));
    }

    [Fact]
    public async Task A_date_the_universe_already_has_is_rejected()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "No Dupes" })).Value.Id;
        Assert.True((await service.CreateTimelineDateAsync(universeId, "Year One")).IsSuccess);

        // Same text, different spacing/casing — two of these would be indistinguishable in a dropdown.
        var duplicate = await service.CreateTimelineDateAsync(universeId, "  year one ");

        Assert.False(duplicate.IsSuccess);
        Assert.Equal(Ardalis.Result.ResultStatus.Conflict, duplicate.Status);
        Assert.Single((await service.ListTimelineDatesAsync(universeId)).Value);
    }

    [Fact]
    public async Task Each_series_keeps_its_own_lane_across_the_date_columns()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Rows Test" })).Value.Id;
        int firstDate = (await service.CreateTimelineDateAsync(universeId, "Act One")).Value.Id;
        int secondDate = (await service.CreateTimelineDateAsync(universeId, "Act Two")).Value.Id;

        int seriesId = await SeedSeriesAsync(scope, "Lane Series");
        int inSeries = await SeedBookAsync(scope, shelfId, "In Series", seriesId);
        int standaloneOne = await SeedBookAsync(scope, shelfId, "Standalone One");
        int standaloneTwo = await SeedBookAsync(scope, shelfId, "Standalone Two");
        await service.AddBooksAsync(universeId, [inSeries, standaloneOne, standaloneTwo]);

        await service.SetBookTimelineDateAsync(universeId, inSeries, firstDate);
        await service.SetBookTimelineDateAsync(universeId, standaloneOne, firstDate);
        await service.SetBookTimelineDateAsync(universeId, standaloneTwo, secondDate);

        var detail = (await service.GetByIdAsync(universeId)).Value;

        Assert.Equal(2, detail.Timeline.Rows.Count);
        foreach (var row in detail.Timeline.Rows)
        {
            // Every lane spans the whole timeline so the view can render it as a matrix row.
            Assert.Equal(
                detail.Timeline.Groups.Select(g => g.TimelineDateId),
                row.Cells.Select(c => c.TimelineDateId));
        }

        var seriesRow = detail.Timeline.Rows.Single(r => r.SeriesId == seriesId);
        Assert.Equal(["In Series"], seriesRow.Cells[0].Entries.Select(e => e.Book.Title));
        Assert.Empty(seriesRow.Cells[1].Entries);

        var standaloneRow = detail.Timeline.Rows.Single(r => r.SeriesId is null);
        Assert.Equal("Standalone", standaloneRow.Label);
        Assert.Equal(["Standalone One"], standaloneRow.Cells[0].Entries.Select(e => e.Book.Title));
        Assert.Equal(["Standalone Two"], standaloneRow.Cells[1].Entries.Select(e => e.Book.Title));
    }

    [Fact]
    public async Task Adding_the_same_book_twice_does_not_duplicate_the_membership()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Idempotent" })).Value.Id;
        int bookId = await SeedBookAsync(scope, shelfId, "Only Once");

        Assert.Equal(1, (await service.AddBooksAsync(universeId, [bookId])).Value);
        Assert.Equal(0, (await service.AddBooksAsync(universeId, [bookId])).Value);

        var detail = (await service.GetByIdAsync(universeId)).Value;
        Assert.Single(detail.Timeline.Entries);
    }

    [Fact]
    public async Task Assigning_a_series_leaves_its_books_off_the_timeline_unless_asked()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "No Auto Sync" })).Value.Id;
        int seriesId = await SeedSeriesAsync(scope, "Standalone Series");
        await SeedBookAsync(scope, shelfId, "Book In Series", seriesId);

        await service.SetSeriesUniverseAsync(seriesId, universeId);

        var detail = (await service.GetByIdAsync(universeId)).Value;
        Assert.Single(detail.Series);
        Assert.Empty(detail.Timeline.Entries);
    }

    [Fact]
    public async Task Assigning_a_series_can_opt_into_adding_its_books()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Opt In" })).Value.Id;
        int seriesId = await SeedSeriesAsync(scope, "Bulk Added Series");
        await SeedBookAsync(scope, shelfId, "First", seriesId);
        await SeedBookAsync(scope, shelfId, "Second", seriesId);

        await service.SetSeriesUniverseAsync(seriesId, universeId, addSeriesBooks: true);

        var detail = (await service.GetByIdAsync(universeId)).Value;
        Assert.Equal(2, detail.Timeline.Entries.Count);
    }

    [Fact]
    public async Task Deleting_a_universe_keeps_its_books_and_series()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Doomed" })).Value.Id;
        int seriesId = await SeedSeriesAsync(scope, "Surviving Series");
        int bookId = await SeedBookAsync(scope, shelfId, "Surviving Book", seriesId);

        await service.SetSeriesUniverseAsync(seriesId, universeId, addSeriesBooks: true);
        Assert.True((await service.DeleteAsync(universeId)).IsSuccess);

        var books = scope.ServiceProvider.GetRequiredService<IRepository<Book>>();
        var seriesRepo = scope.ServiceProvider.GetRequiredService<IRepository<Series>>();
        var memberships = scope.ServiceProvider.GetRequiredService<IRepository<UniverseBook>>();

        Assert.NotNull(await books.FindOneAsync(new SearchOptions<Book> { Query = b => b.Id == bookId }));

        var series = await seriesRepo.FindOneAsync(new SearchOptions<Series> { Query = s => s.Id == seriesId });
        Assert.NotNull(series);
        Assert.Null(series.UniverseId);

        Assert.Equal(0, await memberships.CountAsync(ub => ub.UniverseId == universeId));
    }

    [Fact]
    public async Task Removing_a_book_only_drops_the_membership_and_closes_the_order_gap()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Gap Closing" })).Value.Id;
        int first = await SeedBookAsync(scope, shelfId, "Gap First");
        int middle = await SeedBookAsync(scope, shelfId, "Gap Middle");
        int last = await SeedBookAsync(scope, shelfId, "Gap Last");

        await service.AddBooksAsync(universeId, [first, middle, last]);
        Assert.True((await service.RemoveBookAsync(universeId, middle)).IsSuccess);

        var detail = (await service.GetByIdAsync(universeId)).Value;
        Assert.Equal(["Gap First", "Gap Last"], detail.Timeline.Entries.Select(t => t.Book.Title));
        Assert.Equal([0, 1], detail.Timeline.Entries.Select(t => t.Order));

        var books = scope.ServiceProvider.GetRequiredService<IRepository<Book>>();
        Assert.NotNull(await books.FindOneAsync(new SearchOptions<Book> { Query = b => b.Id == middle }));
    }

    [Fact]
    public async Task Non_administrators_cannot_create_a_universe()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope, isAdministrator: false);

        var result = await service.CreateAsync(new CreateUniverseRequest { Name = "Not Allowed" });

        Assert.False(result.IsSuccess);
        Assert.Equal(Ardalis.Result.ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task Universe_reading_orders_are_hidden_from_the_normal_reading_lists_page()
    {
        using var scope = _fixture.CreateScope();
        var universeService = BuildService(scope);
        var readingListService = BuildReadingListService(scope);

        int universeId = (await universeService.CreateAsync(new CreateUniverseRequest { Name = "Hidden Lists" })).Value.Id;
        await universeService.CreateReadingListAsync(universeId, new CreateReadingListRequest { Name = "Chronological Order" });
        await readingListService.CreateAsync(new CreateReadingListRequest { Name = "My Personal Queue" });

        var visible = (await readingListService.ListAsync()).Value;

        Assert.Contains(visible, l => l.Name == "My Personal Queue");
        Assert.DoesNotContain(visible, l => l.Name == "Chronological Order");

        var detail = (await universeService.GetByIdAsync(universeId)).Value;
        Assert.Single(detail.ReadingLists);
    }

    [Fact]
    public async Task A_new_reading_order_starts_as_the_whole_timeline()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        var readingListService = BuildReadingListService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Prefilled" })).Value.Id;
        int first = await SeedBookAsync(scope, shelfId, "Prefilled One");
        int second = await SeedBookAsync(scope, shelfId, "Prefilled Two");
        await service.AddBooksAsync(universeId, [first, second]);

        int listId = (await service.CreateReadingListAsync(
            universeId,
            new CreateReadingListRequest { Name = "Publication Order" })).Value.Id;

        var list = (await readingListService.GetByIdAsync(listId)).Value;
        Assert.Equal(["Prefilled One", "Prefilled Two"], list.Items.Select(i => i.Book.Title));
    }

    [Fact]
    public async Task A_reading_order_refuses_books_from_outside_its_universe()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        var readingListService = BuildReadingListService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Walled Garden" })).Value.Id;
        int inside = await SeedBookAsync(scope, shelfId, "Inside The Universe");
        int outside = await SeedBookAsync(scope, shelfId, "Some Other Book");
        await service.AddBooksAsync(universeId, [inside]);

        int listId = (await service.CreateReadingListAsync(
            universeId,
            new CreateReadingListRequest { Name = "Chronological" })).Value.Id;

        Assert.False((await readingListService.AddBookAsync(listId, outside)).IsSuccess);

        // The bulk path skips them silently rather than failing the whole call.
        Assert.Equal(0, (await readingListService.AddBooksAsync(listId, [outside])).Value);

        var list = (await readingListService.GetByIdAsync(listId)).Value;
        Assert.Equal(["Inside The Universe"], list.Items.Select(i => i.Book.Title));
    }

    private static UniverseService BuildService(IServiceScope scope, bool isAdministrator = true)
    {
        var sp = scope.ServiceProvider;
        return new UniverseService(
            BuildUserContext(isAdministrator),
            sp.GetRequiredService<IRepository<Universe>>(),
            sp.GetRequiredService<IRepository<UniverseBook>>(),
            sp.GetRequiredService<IRepository<TimelineDate>>(),
            sp.GetRequiredService<IRepository<Series>>(),
            sp.GetRequiredService<IRepository<Book>>(),
            sp.GetRequiredService<IRepository<ReadingList>>(),
            sp.GetRequiredService<IRepository<ReadingListItem>>(),
            sp.GetRequiredService<IRepository<BookProgress>>());
    }

    private static ReadingListService BuildReadingListService(IServiceScope scope)
    {
        var sp = scope.ServiceProvider;
        var storage = new Mock<Shelfwarden.Services.Storage.IStoragePathProvider>();
        return new ReadingListService(
            BuildUserContext(isAdministrator: true),
            sp.GetRequiredService<IRepository<ReadingList>>(),
            sp.GetRequiredService<IRepository<ReadingListItem>>(),
            sp.GetRequiredService<IRepository<Book>>(),
            sp.GetRequiredService<IRepository<BookProgress>>(),
            sp.GetRequiredService<IRepository<UniverseBook>>(),
            storage.Object);
    }

    private static IUserContextService BuildUserContext(bool isAdministrator)
    {
        var userContext = new Mock<IUserContextService>();
        userContext.Setup(x => x.GetCurrentUserId()).Returns("test-user");
        userContext.Setup(x => x.IsAdministrator()).Returns(isAdministrator);
        return userContext.Object;
    }

    private static async Task<int> SeedShelfAsync(IServiceScope scope)
    {
        var shelves = scope.ServiceProvider.GetRequiredService<IRepository<Shelf>>();
        var shelf = await shelves.InsertAsync(new Shelf { Name = $"Shelf {Guid.NewGuid():N}" });
        return shelf.Id;
    }

    private static async Task<int> SeedSeriesAsync(IServiceScope scope, string name)
    {
        var seriesRepo = scope.ServiceProvider.GetRequiredService<IRepository<Series>>();
        var series = await seriesRepo.InsertAsync(new Series
        {
            Name = name,
            NormalizedName = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}",
        });
        return series.Id;
    }

    private static async Task<int> SeedBookAsync(IServiceScope scope, int shelfId, string title, int? seriesId = null)
    {
        var books = scope.ServiceProvider.GetRequiredService<IRepository<Book>>();
        var book = await books.InsertAsync(new Book
        {
            ShelfId = shelfId,
            SeriesId = seriesId,
            Title = title,
            FilePath = $@"C:\Books\{Guid.NewGuid():N}.epub",
            FileFormat = EbookFormat.Epub,
        });
        return book.Id;
    }
}
