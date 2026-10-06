using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shelfwarden.Models.Opds;
using Shelfwarden.Services;
using Shelfwarden.Services.Opds;
using Shelfwarden.Tests.Infrastructure;

namespace Shelfwarden.Tests.Opds;

/// <summary>
/// Feed contents against a real SQLite database. Every test seeds its own shelf and sees only that
/// shelf (as a non-admin would), so tests sharing the fixture's database can't see each other's books.
/// </summary>
public class OpdsFeedServiceTests : IClassFixture<TestDbFixture>
{
    private static readonly XNamespace Atom = OpdsNamespaces.Atom;

    private readonly TestDbFixture fixture;

    public OpdsFeedServiceTests(TestDbFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Root_is_a_navigation_feed_linking_every_section_and_search()
    {
        using var scope = fixture.CreateScope();
        var service = BuildService(scope, visibleShelves: []);

        var feed = (await service.GetRootAsync(new OpdsLinkBuilder())).Value;

        Assert.Equal(OpdsFeedKind.Navigation, feed.Kind);
        Assert.Equal("/opds", Link(feed, OpdsLinkRelations.Self).Href);
        Assert.Equal("/opds", Link(feed, OpdsLinkRelations.Start).Href);
        Assert.DoesNotContain(feed.Links, l => l.Rel == OpdsLinkRelations.Up);

        var search = feed.Links.Where(l => l.Rel == OpdsLinkRelations.Search).ToList();
        Assert.Contains(search, l => l.Type == OpdsMediaTypes.OpenSearchDescription && l.Href == "/opds/opensearch.xml");
        Assert.Contains(search, l => l.Href == "/opds/search?q={searchTerms}");

        Assert.Equal(
            ["/opds/recent", "/opds/all", "/opds/authors", "/opds/series", "/opds/shelves"],
            feed.Entries.Select(e => e.Links.Single().Href));
        Assert.All(feed.Entries, e => Assert.NotEqual(default, e.Updated));
        Assert.Equal(feed.Entries.Count, feed.Entries.Select(e => e.Id).Distinct().Count());
        Assert.Equal(OpdsLinkRelations.SortNew, feed.Entries[0].Links[0].Rel);
    }

    [Fact]
    public async Task Book_entries_carry_metadata_acquisition_and_cover_links()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        int seriesId = await seed.SeriesAsync("Imperial Radch");
        int authorId = await seed.AuthorAsync("Ann Leckie");
        int bookId = await seed.BookAsync(shelfId, "Ancillary Sword", b =>
        {
            b.SeriesId = seriesId;
            b.NumberInSeries = 2;
            b.Description = "<p>Breq has <i>one</i> ship.</p>";
            b.Language = "en";
            b.Publisher = "Orbit";
            b.Isbn = "9780316246651";
            b.PublishedOn = new DateTime(2014, 10, 7, 0, 0, 0, DateTimeKind.Utc);
            b.FileSizeBytes = 123456;
            b.CoverImagePath = "1.png";
        }, authorId);
        await seed.GenreAsync(bookId, "Science Fiction");
        await seed.TagAsync(bookId, "Space Opera");

        var feed = (await BuildService(scope, [shelfId]).GetAllBooksAsync(new OpdsLinkBuilder(), 1)).Value;
        var entry = Assert.Single(feed.Entries);

        Assert.Equal(OpdsFeedKind.Acquisition, feed.Kind);
        Assert.Equal($"urn:shelfwarden:book:{bookId}", entry.Id);
        Assert.Equal("Ancillary Sword", entry.Title);
        var author = Assert.Single(entry.Authors);
        Assert.Equal("Ann Leckie", author.Name);
        Assert.Equal($"/opds/authors/{authorId}", author.Uri);
        Assert.Equal("en", entry.Language);
        Assert.Equal("Orbit", entry.Publisher);
        Assert.Equal("2014-10-07", entry.Issued);
        Assert.Contains("urn:isbn:9780316246651", entry.Identifiers);
        Assert.Equal(["Science Fiction", "Space Opera"], entry.Categories.Select(c => c.Term).Order());
        Assert.Equal("Imperial Radch, book 2\n\nBreq has one ship.", entry.Summary?.Value);

        var acquisition = Link(entry, OpdsLinkRelations.Acquisition);
        Assert.Equal(OpdsMediaTypes.Epub, acquisition.Type);
        Assert.Equal(123456, acquisition.Length);
        Assert.Equal($"/opds/books/{bookId}/download/Ancillary%20Sword.epub", acquisition.Href);

        Assert.Equal("image/png", Link(entry, OpdsLinkRelations.Image).Type);
        Assert.Equal($"/opds/books/{bookId}/thumbnail", Link(entry, OpdsLinkRelations.Thumbnail).Href);
        Assert.Equal($"/opds/series/{seriesId}", Link(entry, OpdsLinkRelations.Related).Href);
    }

    [Fact]
    public async Task Pdf_books_advertise_the_pdf_media_type()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        await seed.BookAsync(shelfId, "A Manual", b =>
        {
            b.FileFormat = EbookFormat.Pdf;
            b.FilePath = $@"C:\Books\{Guid.NewGuid():N}.pdf";
        });

        var entry = Assert.Single((await BuildService(scope, [shelfId]).GetAllBooksAsync(new OpdsLinkBuilder(), 1)).Value.Entries);

        Assert.Equal("application/pdf", Link(entry, OpdsLinkRelations.Acquisition).Type);
    }

    [Fact]
    public async Task Books_without_optional_metadata_or_cover_still_produce_valid_entries()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        await seed.BookAsync(shelfId, "Bare\0 Bones");

        var feed = (await BuildService(scope, [shelfId]).GetAllBooksAsync(new OpdsLinkBuilder(), 1)).Value;
        var entry = Assert.Single(feed.Entries);

        Assert.Equal("Bare Bones", entry.Title);
        Assert.Empty(entry.Authors);
        Assert.Null(entry.Summary);
        Assert.Null(entry.Language);
        Assert.DoesNotContain(entry.Links, l => l.Rel is OpdsLinkRelations.Image or OpdsLinkRelations.Thumbnail);
        Assert.Single(entry.Links, l => l.Rel == OpdsLinkRelations.Acquisition);

        var xmlEntry = XDocument.Parse(OpdsXmlSerializer.Serialize(feed)).Root!.Element(Atom + "entry")!;
        Assert.Equal("Bare Bones", xmlEntry.Element(Atom + "title")!.Value);
    }

    [Fact]
    public async Task Pages_link_to_their_neighbours_and_report_opensearch_counts()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        foreach (string title in new[] { "A", "B", "C", "D", "E" })
        {
            await seed.BookAsync(shelfId, title);
        }

        var service = BuildService(scope, [shelfId], pageSize: 2);
        var links = new OpdsLinkBuilder();

        var first = (await service.GetAllBooksAsync(links, 1)).Value;
        Assert.Equal(["A", "B"], first.Entries.Select(e => e.Title));
        Assert.Equal(5, first.TotalResults);
        Assert.Equal(2, first.ItemsPerPage);
        Assert.Equal(1, first.StartIndex);
        Assert.Equal("/opds/all", Link(first, OpdsLinkRelations.Self).Href);
        Assert.Equal("/opds/all?page=2", Link(first, OpdsLinkRelations.Next).Href);
        Assert.Equal("/opds/all?page=3", Link(first, OpdsLinkRelations.Last).Href);
        Assert.Equal("/opds/all", Link(first, OpdsLinkRelations.First).Href);
        Assert.DoesNotContain(first.Links, l => l.Rel == OpdsLinkRelations.Previous);

        var middle = (await service.GetAllBooksAsync(links, 2)).Value;
        Assert.Equal(["C", "D"], middle.Entries.Select(e => e.Title));
        Assert.Equal(3, middle.StartIndex);
        Assert.Equal("/opds/all", Link(middle, OpdsLinkRelations.Previous).Href);
        Assert.Equal("/opds/all?page=3", Link(middle, OpdsLinkRelations.Next).Href);

        var last = (await service.GetAllBooksAsync(links, 3)).Value;
        Assert.Equal(["E"], last.Entries.Select(e => e.Title));
        Assert.DoesNotContain(last.Links, l => l.Rel == OpdsLinkRelations.Next);
        Assert.Equal("/opds/all?page=2", Link(last, OpdsLinkRelations.Previous).Href);
        Assert.All(last.Links.Where(l => l.Rel is OpdsLinkRelations.First or OpdsLinkRelations.Last or OpdsLinkRelations.Previous),
            l => Assert.Equal(OpdsMediaTypes.AcquisitionFeed, l.Type));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Page_numbers_below_one_mean_the_first_page(int page)
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        await seed.BookAsync(shelfId, "Only");

        var feed = (await BuildService(scope, [shelfId]).GetAllBooksAsync(new OpdsLinkBuilder(), page)).Value;

        Assert.Equal("Only", Assert.Single(feed.Entries).Title);
        Assert.Equal(1, feed.StartIndex);
        Assert.Equal("/opds/all", Link(feed, OpdsLinkRelations.Self).Href);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public async Task Pages_past_the_end_are_empty_and_point_back_to_the_last_real_page(int page)
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        foreach (string title in new[] { "A", "B", "C" })
        {
            await seed.BookAsync(shelfId, title);
        }

        var result = await BuildService(scope, [shelfId], pageSize: 1).GetAllBooksAsync(new OpdsLinkBuilder(), page);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Entries);
        Assert.Equal(3, result.Value.TotalResults);
        Assert.Equal("/opds/all?page=3", Link(result.Value, OpdsLinkRelations.Previous).Href);
        Assert.DoesNotContain(result.Value.Links, l => l.Rel == OpdsLinkRelations.Next);
    }

    [Fact]
    public async Task An_empty_shelf_gives_an_empty_feed_without_paging_links()
    {
        using var scope = fixture.CreateScope();
        int shelfId = await new Seeder(scope).ShelfAsync();

        var feed = (await BuildService(scope, [shelfId]).GetShelfBooksAsync(new OpdsLinkBuilder(), shelfId, 1)).Value;

        Assert.Empty(feed.Entries);
        Assert.Equal(0, feed.TotalResults);
        Assert.DoesNotContain(feed.Links, l => l.Rel is OpdsLinkRelations.First or OpdsLinkRelations.Last or OpdsLinkRelations.Next or OpdsLinkRelations.Previous);
        Assert.Equal("/opds/shelves", Link(feed, OpdsLinkRelations.Up).Href);
    }

    [Fact]
    public async Task Recent_lists_newest_first()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        await seed.BookAsync(shelfId, "Old", b => b.CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await seed.BookAsync(shelfId, "New", b => b.CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var feed = (await BuildService(scope, [shelfId]).GetRecentAsync(new OpdsLinkBuilder(), 1)).Value;

        Assert.Equal(["New", "Old"], feed.Entries.Select(e => e.Title));
    }

    [Fact]
    public async Task Pseudonyms_are_listed_as_authors_and_note_who_they_belong_to()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        string token = Seeder.Token();
        int primary = await seed.AuthorAsync($"Iain Banks {token}");
        int pseudonym = await seed.AuthorAsync($"Iain M. Banks {token}", primaryAuthorId: primary);
        await seed.BookAsync(shelfId, "The Wasp Factory", primary);
        await seed.BookAsync(shelfId, "Consider Phlebas", pseudonym);
        await seed.AuthorAsync($"Nobody On This Shelf {token}");

        var service = BuildService(scope, [shelfId]);
        var feed = (await service.GetAuthorsAsync(new OpdsLinkBuilder(), 1)).Value;

        Assert.Equal(OpdsFeedKind.Navigation, feed.Kind);
        Assert.Equal([$"Iain Banks {token}", $"Iain M. Banks {token}"], feed.Entries.Select(e => e.Title));
        Assert.Equal($"1 book. Also writes as Iain M. Banks {token}.", feed.Entries[0].Content?.Value);
        Assert.Equal($"1 book. Pseudonym of Iain Banks {token}.", feed.Entries[1].Content?.Value);

        // Each author feed holds only the books credited to that exact name.
        var pseudonymBooks = (await service.GetAuthorBooksAsync(new OpdsLinkBuilder(), pseudonym, 1)).Value;
        Assert.Equal(["Consider Phlebas"], pseudonymBooks.Entries.Select(e => e.Title));
        Assert.Equal("/opds/authors", Link(pseudonymBooks, OpdsLinkRelations.Up).Href);
    }

    [Fact]
    public async Task Search_matches_title_author_pseudonym_and_series()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        string token = Seeder.Token();
        int primary = await seed.AuthorAsync($"Mary Westmacott Primary {token}");
        int pseudonym = await seed.AuthorAsync($"Pen Name {token}", primaryAuthorId: primary);
        int seriesId = await seed.SeriesAsync($"Saga {token}");
        await seed.BookAsync(shelfId, $"Title Hit {token}");
        await seed.BookAsync(shelfId, "Written Under Pen Name", pseudonym);
        await seed.BookAsync(shelfId, "Series Member", b => b.SeriesId = seriesId);
        await seed.BookAsync(shelfId, "Unrelated");

        var service = BuildService(scope, [shelfId]);
        var links = new OpdsLinkBuilder();

        async Task<string[]> TitlesFor(string query) =>
            [.. (await service.SearchAsync(links, query, 1)).Value.Entries.Select(e => e.Title).Order()];

        Assert.Equal([$"Title Hit {token}"], await TitlesFor($"TITLE hit {token}"));
        Assert.Equal(["Written Under Pen Name"], await TitlesFor($"pen name {token}"));
        Assert.Equal(["Written Under Pen Name"], await TitlesFor($"westmacott primary {token}"));
        Assert.Equal(["Series Member"], await TitlesFor($"saga {token}"));

        var feed = (await service.SearchAsync(links, $"saga {token}", 1)).Value;
        Assert.Equal(OpdsFeedKind.Acquisition, feed.Kind);
        Assert.Equal($"/opds/search?q={Uri.EscapeDataString($"saga {token}")}", Link(feed, OpdsLinkRelations.Self).Href);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_search_returns_an_empty_feed(string? query)
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        await seed.BookAsync(shelfId, "Anything");

        var feed = (await BuildService(scope, [shelfId]).SearchAsync(new OpdsLinkBuilder(), query, 1)).Value;

        Assert.Empty(feed.Entries);
        Assert.Equal(0, feed.TotalResults);
    }

    [Fact]
    public async Task Search_treats_wildcards_and_quotes_literally()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        await seed.BookAsync(shelfId, "100% Effort");
        await seed.BookAsync(shelfId, "Ender's Game");
        await seed.BookAsync(shelfId, "Plain");

        var service = BuildService(scope, [shelfId]);
        var links = new OpdsLinkBuilder();

        Assert.Equal(["100% Effort"], (await service.SearchAsync(links, "100%", 1)).Value.Entries.Select(e => e.Title));
        Assert.Equal(["Ender's Game"], (await service.SearchAsync(links, "ender's", 1)).Value.Entries.Select(e => e.Title));
        Assert.Empty((await service.SearchAsync(links, "_", 1)).Value.Entries);
        Assert.Equal(["100% Effort"], (await service.SearchAsync(links, "%", 1)).Value.Entries.Select(e => e.Title));
    }

    [Fact]
    public async Task Search_finds_non_ascii_titles_as_typed()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        await seed.BookAsync(shelfId, "Émile et les Märchen");

        var entries = (await BuildService(scope, [shelfId]).SearchAsync(new OpdsLinkBuilder(), "Märchen", 1)).Value.Entries;

        Assert.Equal("Émile et les Märchen", Assert.Single(entries).Title);
    }

    [Fact]
    public async Task Books_on_shelves_the_user_cannot_access_never_appear()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int visibleShelf = await seed.ShelfAsync();
        int hiddenShelf = await seed.ShelfAsync();
        string token = Seeder.Token();
        int authorId = await seed.AuthorAsync($"Shared Author {token}");
        await seed.BookAsync(visibleShelf, $"Visible {token}", authorId);
        await seed.BookAsync(hiddenShelf, $"Hidden {token}", authorId);

        var service = BuildService(scope, [visibleShelf]);
        var links = new OpdsLinkBuilder();

        Assert.Equal([$"Visible {token}"], (await service.GetAllBooksAsync(links, 1)).Value.Entries.Select(e => e.Title));
        Assert.Equal([$"Visible {token}"], (await service.SearchAsync(links, token, 1)).Value.Entries.Select(e => e.Title));
        Assert.Equal([$"Visible {token}"], (await service.GetAuthorBooksAsync(links, authorId, 1)).Value.Entries.Select(e => e.Title));
        Assert.Equal("1 book", (await service.GetAuthorsAsync(links, 1)).Value.Entries.Single().Content?.Value);

        var shelves = (await service.GetShelvesAsync(links)).Value;
        Assert.Equal([$"urn:shelfwarden:shelf:{visibleShelf}"], shelves.Entries.Select(e => e.Id));
        Assert.Equal(ResultStatus.NotFound, (await service.GetShelfBooksAsync(links, hiddenShelf, 1)).Status);
    }

    [Fact]
    public async Task Unknown_author_series_and_shelf_are_not_found()
    {
        using var scope = fixture.CreateScope();
        var service = BuildService(scope, []);
        var links = new OpdsLinkBuilder();

        Assert.Equal(ResultStatus.NotFound, (await service.GetAuthorBooksAsync(links, int.MaxValue, 1)).Status);
        Assert.Equal(ResultStatus.NotFound, (await service.GetSeriesBooksAsync(links, int.MaxValue, 1)).Status);
        Assert.Equal(ResultStatus.NotFound, (await service.GetShelfBooksAsync(links, int.MaxValue, 1)).Status);
    }

    [Fact]
    public async Task Series_books_follow_reading_order()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        int seriesId = await seed.SeriesAsync("Ordered");
        await seed.BookAsync(shelfId, "Third", b => { b.SeriesId = seriesId; b.NumberInSeries = 3; });
        await seed.BookAsync(shelfId, "First", b => { b.SeriesId = seriesId; b.NumberInSeries = 1; });
        await seed.BookAsync(shelfId, "Novella", b => { b.SeriesId = seriesId; b.NumberInSeries = 1.5m; });

        var feed = (await BuildService(scope, [shelfId]).GetSeriesBooksAsync(new OpdsLinkBuilder(), seriesId, 1)).Value;

        Assert.Equal(["First", "Novella", "Third"], feed.Entries.Select(e => e.Title));
        Assert.Equal("Ordered, book 1.5", feed.Entries[1].Summary?.Value);
    }

    [Fact]
    public async Task Key_in_url_and_path_base_carry_through_to_book_links()
    {
        using var scope = fixture.CreateScope();
        var seed = new Seeder(scope);
        int shelfId = await seed.ShelfAsync();
        int bookId = await seed.BookAsync(shelfId, "Linked", b => b.CoverImagePath = "x.jpg");

        var feed = (await BuildService(scope, [shelfId]).GetAllBooksAsync(new OpdsLinkBuilder("/sw", "k3y"), 1)).Value;
        var entry = Assert.Single(feed.Entries);

        Assert.Equal("/sw/opds/key/k3y/all", Link(feed, OpdsLinkRelations.Self).Href);
        Assert.StartsWith($"/sw/opds/key/k3y/books/{bookId}/download/", Link(entry, OpdsLinkRelations.Acquisition).Href);
        Assert.Equal($"/sw/opds/key/k3y/books/{bookId}/cover", Link(entry, OpdsLinkRelations.Image).Href);
    }

    private static OpdsLink Link(OpdsFeed feed, string rel) => feed.Links.Single(l => l.Rel == rel);

    private static OpdsLink Link(OpdsEntry entry, string rel) => entry.Links.Single(l => l.Rel == rel);

    private static OpdsFeedService BuildService(IServiceScope scope, IReadOnlyCollection<int> visibleShelves, int pageSize = 50)
    {
        var shelfIds = visibleShelves.ToHashSet();
        var access = new Mock<IShelfAccessService>();
        access.Setup(x => x.GetAccessibleShelfIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(shelfIds);
        access.Setup(x => x.CanAccessShelfAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => shelfIds.Contains(id));

        var sp = scope.ServiceProvider;
        return new OpdsFeedService(
            NullLogger<OpdsFeedService>.Instance,
            Options.Create(new OpdsOptions { PageSize = pageSize }),
            TimeProvider.System,
            access.Object,
            sp.GetRequiredService<IRepository<Book>>(),
            sp.GetRequiredService<IRepository<Author>>(),
            sp.GetRequiredService<IRepository<Series>>(),
            sp.GetRequiredService<IRepository<Shelf>>());
    }

    private sealed class Seeder(IServiceScope scope)
    {
        private T Repo<T>() where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

        public static string Token() => Guid.NewGuid().ToString("N")[..8];

        public async Task<int> ShelfAsync() =>
            (await Repo<IRepository<Shelf>>().InsertAsync(new Shelf { Name = $"Shelf {Token()}" })).Id;

        public async Task<int> SeriesAsync(string name) =>
            (await Repo<IRepository<Series>>().InsertAsync(new Series
            {
                Name = name,
                NormalizedName = $"{name.ToLowerInvariant()}-{Token()}",
            })).Id;

        /// <summary>NormalizedName mirrors production (lower-cased name), so names must be unique per database.</summary>
        public async Task<int> AuthorAsync(string name, int? primaryAuthorId = null) =>
            (await Repo<IRepository<Author>>().InsertAsync(new Author
            {
                Name = name,
                NormalizedName = name.ToLowerInvariant(),
                PrimaryAuthorId = primaryAuthorId,
            })).Id;

        public async Task<int> BookAsync(int shelfId, string title, Action<Book>? configure = null, params int[] authorIds)
        {
            var book = new Book
            {
                ShelfId = shelfId,
                Title = title,
                FilePath = $@"C:\Books\{Guid.NewGuid():N}.epub",
                FileFormat = EbookFormat.Epub,
            };
            configure?.Invoke(book);
            book = await Repo<IRepository<Book>>().InsertAsync(book);

            var bookAuthors = Repo<IRepository<BookAuthor>>();
            for (int i = 0; i < authorIds.Length; i++)
            {
                await bookAuthors.InsertAsync(new BookAuthor { BookId = book.Id, AuthorId = authorIds[i], Position = i });
            }

            return book.Id;
        }

        public Task<int> BookAsync(int shelfId, string title, params int[] authorIds) =>
            BookAsync(shelfId, title, null, authorIds);

        public async Task GenreAsync(int bookId, string name)
        {
            var genre = await Repo<IRepository<Genre>>().InsertAsync(new Genre { Name = name, NormalizedName = $"{name.ToLowerInvariant()}-{Token()}" });
            await Repo<IRepository<BookGenre>>().InsertAsync(new BookGenre { BookId = bookId, GenreId = genre.Id });
        }

        public async Task TagAsync(int bookId, string name)
        {
            var tag = await Repo<IRepository<Tag>>().InsertAsync(new Tag { Name = name, NormalizedName = $"{name.ToLowerInvariant()}-{Token()}" });
            await Repo<IRepository<BookTag>>().InsertAsync(new BookTag { BookId = bookId, TagId = tag.Id });
        }
    }
}
