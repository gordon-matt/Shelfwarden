using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shelfwarden.Models;
using Shelfwarden.Services;
using Shelfwarden.Services.Auth;
using Shelfwarden.Tests.Infrastructure;

namespace Shelfwarden.Tests;

/// <summary>
/// Covers the rules that make universes different from collections / reading lists: the timeline
/// is ordered by <see cref="UniverseBook.TimelineOrder"/> and never by the free-text date, book
/// and series membership never sync themselves, and deleting a universe must not take any books
/// or series with it.
/// </summary>
public class UniverseServiceTests : IClassFixture<TestDbFixture>
{
    private readonly TestDbFixture _fixture;

    public UniverseServiceTests(TestDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Timeline_is_ordered_by_TimelineOrder_not_by_TimelineDate()
    {
        using var scope = _fixture.CreateScope();
        var service = BuildService(scope);
        int shelfId = await SeedShelfAsync(scope);

        int universeId = (await service.CreateAsync(new CreateUniverseRequest { Name = "Ordering Test" })).Value.Id;
        int earlyDate = await SeedBookAsync(scope, shelfId, "Ten Thousand BC");
        int lateDate = await SeedBookAsync(scope, shelfId, "Five Hundred BC");

        await service.AddBooksAsync(universeId, [earlyDate, lateDate]);

        // Dates that sort the "wrong" way round on purpose — the order must ignore them entirely.
        await service.SetTimelineDateAsync(universeId, earlyDate, "10,000 BC");
        await service.SetTimelineDateAsync(universeId, lateDate, "500 BC");

        var detail = (await service.GetByIdAsync(universeId)).Value;
        Assert.Equal(["Ten Thousand BC", "Five Hundred BC"], detail.Timeline.Select(t => t.Book.Title));

        var reversed = detail.Timeline.Select(t => t.Id).Reverse().ToList();
        await service.ReorderTimelineAsync(universeId, reversed);

        detail = (await service.GetByIdAsync(universeId)).Value;
        Assert.Equal(["Five Hundred BC", "Ten Thousand BC"], detail.Timeline.Select(t => t.Book.Title));
        Assert.Equal([0, 1], detail.Timeline.Select(t => t.TimelineOrder));
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
        Assert.Single(detail.Timeline);
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
        Assert.Empty(detail.Timeline);
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
        Assert.Equal(2, detail.Timeline.Count);
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
        Assert.Equal([0, 1], detail.Timeline.Select(t => t.TimelineOrder));

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
