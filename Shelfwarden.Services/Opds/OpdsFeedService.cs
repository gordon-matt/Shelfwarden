using System.Globalization;
using System.Linq.Expressions;
using LinqKit;
using Microsoft.Extensions.Options;
using Shelfwarden.Models.Opds;

namespace Shelfwarden.Services.Opds;

public class OpdsFeedService(
    ILogger<OpdsFeedService> logger,
    IOptions<OpdsOptions> options,
    TimeProvider timeProvider,
    IShelfAccessService shelfAccessService,
    IRepository<Book> bookRepository,
    IRepository<Author> authorRepository,
    IRepository<Series> seriesRepository,
    IRepository<Shelf> shelfRepository) : IOpdsFeedService
{
    public const int MaxSearchLength = 200;

    private const string CatalogTitle = "Shelfwarden";
    private const string ProjectUri = "https://github.com/gordon-matt/Shelfwarden";

    private static readonly Expression<Func<Book, BookRow>> BookRowProjection = b => new BookRow(
        b.Id,
        b.Title,
        b.Description,
        b.Language,
        b.Publisher,
        b.Isbn,
        b.PublishedOn,
        b.FileFormat,
        b.FileSizeBytes,
        b.FilePath,
        b.CoverImagePath,
        b.CreatedAt,
        b.UpdatedAt,
        b.SeriesId,
        b.Series != null ? b.Series.Name : null,
        b.NumberInSeries,
        b.BookAuthors
            .OrderBy(ba => ba.Position)
            .ThenBy(ba => ba.Id)
            .Select(ba => new AuthorRef(ba.AuthorId, ba.Author.Name))
            .ToList(),
        b.BookGenres.Select(bg => bg.Genre.Name).ToList(),
        b.BookTags.Select(bt => bt.Tag.Name).ToList());

    private static readonly Expression<Func<IQueryable<Book>, IQueryable<Book>>> ByTitle =
        q => q.OrderBy(b => b.SortTitle ?? b.Title).ThenBy(b => b.Id);

    private static readonly Expression<Func<IQueryable<Book>, IQueryable<Book>>> ByNewest =
        q => q.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id);

    private static readonly Expression<Func<IQueryable<Book>, IQueryable<Book>>> BySeriesOrder =
        q => q.OrderBy(b => b.NumberInSeries == null)
            .ThenBy(b => b.NumberInSeries)
            .ThenBy(b => b.SortTitle ?? b.Title)
            .ThenBy(b => b.Id);

    // Standalone books after series, series grouped together — matches the web author page.
    private static readonly Expression<Func<IQueryable<Book>, IQueryable<Book>>> ByAuthorOrder =
        q => q.OrderBy(b => b.SeriesId == null)
            .ThenBy(b => b.Series!.NormalizedName)
            .ThenBy(b => b.NumberInSeries)
            .ThenBy(b => b.SortTitle ?? b.Title)
            .ThenBy(b => b.Id);

    public Task<Result<OpdsFeed>> GetRootAsync(OpdsLinkBuilder links, CancellationToken cancellationToken = default)
    {
        var feed = CreateFeed(OpdsFeedKind.Navigation, "urn:shelfwarden:root", CatalogTitle, links, links.Root(), up: null);

        feed.Entries.Add(NavigationEntry("urn:shelfwarden:recent", "Recently added", "The newest books in the library.",
            links.Recent(), OpdsMediaTypes.AcquisitionFeed, OpdsLinkRelations.SortNew));
        feed.Entries.Add(NavigationEntry("urn:shelfwarden:all", "All books", "Every book, by title.",
            links.AllBooks(), OpdsMediaTypes.AcquisitionFeed));
        feed.Entries.Add(NavigationEntry("urn:shelfwarden:authors", "Authors", "Browse books by author.",
            links.Authors(), OpdsMediaTypes.NavigationFeed));
        feed.Entries.Add(NavigationEntry("urn:shelfwarden:series", "Series", "Browse books by series.",
            links.SeriesList(), OpdsMediaTypes.NavigationFeed));
        feed.Entries.Add(NavigationEntry("urn:shelfwarden:shelves", "Shelves", "Browse books by shelf.",
            links.Shelves(), OpdsMediaTypes.NavigationFeed));

        return Task.FromResult(Result.Success(feed));
    }

    public Task<Result<OpdsFeed>> GetRecentAsync(OpdsLinkBuilder links, int page, CancellationToken cancellationToken = default) =>
        BuildBookFeedAsync(
            links,
            "urn:shelfwarden:recent",
            "Recently added",
            links.Recent,
            filter: null,
            ByNewest,
            page,
            cancellationToken);

    public Task<Result<OpdsFeed>> GetAllBooksAsync(OpdsLinkBuilder links, int page, CancellationToken cancellationToken = default) =>
        BuildBookFeedAsync(
            links,
            "urn:shelfwarden:all",
            "All books",
            links.AllBooks,
            filter: null,
            ByTitle,
            page,
            cancellationToken);

    public async Task<Result<OpdsFeed>> GetAuthorsAsync(OpdsLinkBuilder links, int page, CancellationToken cancellationToken = default)
    {
        try
        {
            var shelfIds = await GetVisibleShelfIdsAsync(cancellationToken);
            var (pageNumber, pageSize) = NormalisePaging(page);

            var rows = await authorRepository.FindAsync(
                new SearchOptions<Author>
                {
                    Query = a => a.BookAuthors.Any(ba => shelfIds.Contains(ba.Book.ShelfId)),
                    OrderBy = q => q.OrderBy(a => a.NormalizedName).ThenBy(a => a.Id),
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    CancellationToken = cancellationToken,
                },
                a => new AuthorRow(
                    a.Id,
                    a.Name,
                    a.PrimaryAuthor != null ? a.PrimaryAuthor.Name : null,
                    a.Pseudonyms.OrderBy(p => p.NormalizedName).Select(p => p.Name).ToList(),
                    a.BookAuthors.Count(ba => shelfIds.Contains(ba.Book.ShelfId))));

            var feed = CreateFeed(OpdsFeedKind.Navigation, "urn:shelfwarden:authors", "Authors", links, links.Authors(pageNumber), links.Root());
            foreach (var row in rows)
            {
                feed.Entries.Add(NavigationEntry(
                    $"urn:shelfwarden:author:{row.Id}",
                    OpdsTextSanitizer.Clean(row.Name),
                    DescribeAuthor(row),
                    links.Author(row.Id),
                    OpdsMediaTypes.AcquisitionFeed));
            }

            AddPaging(feed, rows.ItemCount, pageNumber, pageSize, links.Authors);
            return Result.Success(feed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build OPDS authors feed");
            return Result.Error("Could not load authors.");
        }
    }

    public async Task<Result<OpdsFeed>> GetAuthorBooksAsync(OpdsLinkBuilder links, int authorId, int page, CancellationToken cancellationToken = default)
    {
        string? name = await authorRepository.FindOneAsync(
            new SearchOptions<Author> { Query = a => a.Id == authorId, CancellationToken = cancellationToken },
            a => a.Name);

        return name is null
            ? Result.NotFound()
            : await BuildBookFeedAsync(
                links,
                $"urn:shelfwarden:author:{authorId}",
                OpdsTextSanitizer.Clean(name),
                p => links.Author(authorId, p),
                b => b.BookAuthors.Any(ba => ba.AuthorId == authorId),
                ByAuthorOrder,
                page,
                cancellationToken,
                up: links.Authors());
    }

    public async Task<Result<OpdsFeed>> GetSeriesListAsync(OpdsLinkBuilder links, int page, CancellationToken cancellationToken = default)
    {
        try
        {
            var shelfIds = await GetVisibleShelfIdsAsync(cancellationToken);
            var (pageNumber, pageSize) = NormalisePaging(page);

            var rows = await seriesRepository.FindAsync(
                new SearchOptions<Series>
                {
                    Query = s => s.Books.Any(b => shelfIds.Contains(b.ShelfId)),
                    OrderBy = q => q.OrderBy(s => s.NormalizedName).ThenBy(s => s.Id),
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    CancellationToken = cancellationToken,
                },
                s => new { s.Id, s.Name, BookCount = s.Books.Count(b => shelfIds.Contains(b.ShelfId)) });

            var feed = CreateFeed(OpdsFeedKind.Navigation, "urn:shelfwarden:series", "Series", links, links.SeriesList(pageNumber), links.Root());
            foreach (var row in rows)
            {
                feed.Entries.Add(NavigationEntry(
                    $"urn:shelfwarden:series:{row.Id}",
                    OpdsTextSanitizer.Clean(row.Name),
                    CountBooks(row.BookCount),
                    links.Series(row.Id),
                    OpdsMediaTypes.AcquisitionFeed));
            }

            AddPaging(feed, rows.ItemCount, pageNumber, pageSize, links.SeriesList);
            return Result.Success(feed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build OPDS series feed");
            return Result.Error("Could not load series.");
        }
    }

    public async Task<Result<OpdsFeed>> GetSeriesBooksAsync(OpdsLinkBuilder links, int seriesId, int page, CancellationToken cancellationToken = default)
    {
        string? name = await seriesRepository.FindOneAsync(
            new SearchOptions<Series> { Query = s => s.Id == seriesId, CancellationToken = cancellationToken },
            s => s.Name);

        return name is null
            ? Result.NotFound()
            : await BuildBookFeedAsync(
                links,
                $"urn:shelfwarden:series:{seriesId}",
                OpdsTextSanitizer.Clean(name),
                p => links.Series(seriesId, p),
                b => b.SeriesId == seriesId,
                BySeriesOrder,
                page,
                cancellationToken,
                up: links.SeriesList());
    }

    public async Task<Result<OpdsFeed>> GetShelvesAsync(OpdsLinkBuilder links, CancellationToken cancellationToken = default)
    {
        try
        {
            var shelfIds = await GetVisibleShelfIdsAsync(cancellationToken);
            var rows = await shelfRepository.FindAsync(
                new SearchOptions<Shelf>
                {
                    Query = s => shelfIds.Contains(s.Id),
                    OrderBy = q => q.OrderBy(s => s.Name).ThenBy(s => s.Id),
                    CancellationToken = cancellationToken,
                },
                s => new { s.Id, s.Name, BookCount = s.Books.Count() });

            var feed = CreateFeed(OpdsFeedKind.Navigation, "urn:shelfwarden:shelves", "Shelves", links, links.Shelves(), links.Root());
            foreach (var row in rows)
            {
                feed.Entries.Add(NavigationEntry(
                    $"urn:shelfwarden:shelf:{row.Id}",
                    OpdsTextSanitizer.Clean(row.Name),
                    CountBooks(row.BookCount),
                    links.Shelf(row.Id),
                    OpdsMediaTypes.AcquisitionFeed));
            }

            return Result.Success(feed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build OPDS shelves feed");
            return Result.Error("Could not load shelves.");
        }
    }

    public async Task<Result<OpdsFeed>> GetShelfBooksAsync(OpdsLinkBuilder links, int shelfId, int page, CancellationToken cancellationToken = default)
    {
        // An inaccessible shelf reads as missing so its existence (and name) doesn't leak.
        if (!await shelfAccessService.CanAccessShelfAsync(shelfId, cancellationToken))
        {
            return Result.NotFound();
        }

        string? name = await shelfRepository.FindOneAsync(
            new SearchOptions<Shelf> { Query = s => s.Id == shelfId, CancellationToken = cancellationToken },
            s => s.Name);

        return name is null
            ? Result.NotFound()
            : await BuildBookFeedAsync(
                links,
                $"urn:shelfwarden:shelf:{shelfId}",
                OpdsTextSanitizer.Clean(name),
                p => links.Shelf(shelfId, p),
                b => b.ShelfId == shelfId,
                ByTitle,
                page,
                cancellationToken,
                up: links.Shelves());
    }

    public Task<Result<OpdsFeed>> SearchAsync(OpdsLinkBuilder links, string? query, int page, CancellationToken cancellationToken = default)
    {
        string trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length > MaxSearchLength)
        {
            trimmed = trimmed[..MaxSearchLength];
        }

        string title = trimmed.Length == 0 ? "Search" : $"Search: {OpdsTextSanitizer.Clean(trimmed)}";

        if (trimmed.Length == 0)
        {
            var empty = CreateFeed(OpdsFeedKind.Acquisition, "urn:shelfwarden:search", title, links, links.Search(string.Empty), links.Root());
            AddPaging(empty, 0, 1, options.Value.EffectivePageSize, _ => links.Search(string.Empty));
            return Task.FromResult(Result.Success(empty));
        }

        string upper = trimmed.ToUpperInvariant();
        string lower = trimmed.ToLowerInvariant();

        return BuildBookFeedAsync(
            links,
            "urn:shelfwarden:search",
            title,
            p => links.Search(trimmed, p),
            // The as-typed comparisons cover providers whose UPPER() only folds ASCII (SQLite).
            b => b.Title.ToUpper().Contains(upper)
                || b.Title.Contains(trimmed)
                || (b.Series != null && (b.Series.Name.ToUpper().Contains(upper) || b.Series.Name.Contains(trimmed)))
                || b.BookAuthors.Any(ba =>
                    ba.Author.NormalizedName.Contains(lower)
                    || (ba.Author.PrimaryAuthor != null && ba.Author.PrimaryAuthor.NormalizedName.Contains(lower))),
            ByTitle,
            page,
            cancellationToken,
            up: links.Root());
    }

    public OpenSearchDescription GetOpenSearchDescription(OpdsLinkBuilder links) => new()
    {
        ShortName = CatalogTitle,
        Description = "Search books by title, author or series.",
        Urls =
        [
            new OpenSearchUrl { Type = OpdsMediaTypes.Atom, Template = links.SearchTemplate() },
            new OpenSearchUrl { Type = OpdsMediaTypes.AcquisitionFeed, Template = links.SearchTemplate() },
        ],
    };

    private static void AddPaging(OpdsFeed feed, int totalCount, int page, int pageSize, Func<int, string> pageUrl)
    {
        string type = feed.MediaType;
        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        feed.TotalResults = totalCount;
        feed.ItemsPerPage = pageSize;
        feed.StartIndex = ((page - 1) * pageSize) + 1;

        if (totalPages > 1)
        {
            feed.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.First, Href = pageUrl(1), Type = type });
        }

        if (page > 1 && totalPages > 0)
        {
            // Past the end, "previous" jumps back to the real last page rather than an equally empty one.
            feed.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Previous, Href = pageUrl(Math.Min(page - 1, totalPages)), Type = type });
        }

        if (page < totalPages)
        {
            feed.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Next, Href = pageUrl(page + 1), Type = type });
        }

        if (totalPages > 1)
        {
            feed.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Last, Href = pageUrl(totalPages), Type = type });
        }
    }

    private static string CountBooks(int count) => count == 1 ? "1 book" : $"{count} books";

    private static string DescribeAuthor(AuthorRow row)
    {
        string text = CountBooks(row.BookCount);
        if (row.PrimaryAuthorName is not null)
        {
            return $"{text}. Pseudonym of {OpdsTextSanitizer.Clean(row.PrimaryAuthorName)}.";
        }

        return row.PseudonymNames.Count > 0
            ? $"{text}. Also writes as {string.Join(", ", row.PseudonymNames.Select(OpdsTextSanitizer.Clean))}."
            : text;
    }

    private static string FormatSeriesNumber(decimal number) =>
        number.ToString("0.##", CultureInfo.InvariantCulture);

    private static string? ImageMediaType(string coverPath) => Path.GetExtension(coverPath).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => null,
    };

    private OpdsEntry NavigationEntry(string id, string title, string description, string href, string type, string rel = OpdsLinkRelations.Subsection) => new()
    {
        Id = id,
        Title = title,
        Updated = timeProvider.GetUtcNow().UtcDateTime,
        Content = new OpdsText { Value = description },
        Links = [new OpdsLink { Rel = rel, Href = href, Type = type }],
    };

    private static OpdsEntry ToEntry(BookRow row, OpdsLinkBuilder links)
    {
        var entry = new OpdsEntry
        {
            Id = $"urn:shelfwarden:book:{row.Id}",
            Title = OpdsTextSanitizer.Clean(row.Title),
            Updated = row.UpdatedAt ?? row.CreatedAt,
            Language = OpdsTextSanitizer.CleanOrNull(row.Language),
            Publisher = OpdsTextSanitizer.CleanOrNull(row.Publisher),
            Issued = row.PublishedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        foreach (var author in row.Authors)
        {
            string? name = OpdsTextSanitizer.CleanOrNull(author.Name);
            if (name is not null)
            {
                entry.Authors.Add(new OpdsAuthor { Name = name, Uri = links.Author(author.Id) });
            }
        }

        string? isbn = OpdsTextSanitizer.CleanOrNull(row.Isbn);
        if (isbn is not null)
        {
            entry.Identifiers.Add($"urn:isbn:{isbn}");
        }

        foreach (string category in row.Genres.Concat(row.Tags)
            .Select(OpdsTextSanitizer.CleanOrNull)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            entry.Categories.Add(new OpdsCategory { Term = category, Label = category });
        }

        string? seriesLine = row.SeriesName is null
            ? null
            : row.NumberInSeries is decimal n
                ? $"{OpdsTextSanitizer.Clean(row.SeriesName)}, book {FormatSeriesNumber(n)}"
                : OpdsTextSanitizer.Clean(row.SeriesName);

        string summary = string.Join("\n\n", new[] { seriesLine, OpdsTextSanitizer.HtmlToPlainText(row.Description) }
            .Where(s => !string.IsNullOrEmpty(s)));

        if (summary.Length > 0)
        {
            entry.Summary = new OpdsText { Value = summary };
        }

        string fileName = BookFileNames.BuildDownloadFileName(row.Id, row.Title, row.FilePath, row.FileFormat);
        entry.Links.Add(new OpdsLink
        {
            Rel = OpdsLinkRelations.Acquisition,
            Href = links.Download(row.Id, fileName),
            Type = OpdsMediaTypes.ForFormat(row.FileFormat),
            Title = row.FileFormat.ToString().ToUpperInvariant(),
            Length = row.FileSizeBytes,
        });

        if (!string.IsNullOrEmpty(row.CoverImagePath))
        {
            entry.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Image, Href = links.Cover(row.Id), Type = ImageMediaType(row.CoverImagePath) });
            entry.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Thumbnail, Href = links.Thumbnail(row.Id), Type = "image/jpeg" });
        }

        if (row.SeriesId is int seriesId && row.SeriesName is not null)
        {
            entry.Links.Add(new OpdsLink
            {
                Rel = OpdsLinkRelations.Related,
                Href = links.Series(seriesId),
                Type = OpdsMediaTypes.AcquisitionFeed,
                Title = $"More in {OpdsTextSanitizer.Clean(row.SeriesName)}",
            });
        }

        return entry;
    }

    private async Task<Result<OpdsFeed>> BuildBookFeedAsync(
        OpdsLinkBuilder links,
        string id,
        string title,
        Func<int, string> pageUrl,
        Expression<Func<Book, bool>>? filter,
        Expression<Func<IQueryable<Book>, IQueryable<Book>>> orderBy,
        int page,
        CancellationToken cancellationToken,
        string? up = null)
    {
        try
        {
            var shelfIds = await GetVisibleShelfIdsAsync(cancellationToken);
            var (pageNumber, pageSize) = NormalisePaging(page);

            var predicate = PredicateBuilder.New<Book>(b => shelfIds.Contains(b.ShelfId));
            if (filter is not null)
            {
                predicate = predicate.And(filter);
            }

            var rows = await bookRepository.FindAsync(
                new SearchOptions<Book>
                {
                    Query = predicate,
                    OrderBy = orderBy,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    CancellationToken = cancellationToken,
                },
                BookRowProjection);

            var feed = CreateFeed(OpdsFeedKind.Acquisition, id, title, links, pageUrl(pageNumber), up ?? links.Root());
            feed.Entries.AddRange(rows.Select(r => ToEntry(r, links)));
            AddPaging(feed, rows.ItemCount, pageNumber, pageSize, pageUrl);
            return Result.Success(feed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build OPDS feed {FeedId}", id);
            return Result.Error("Could not load books.");
        }
    }

    private OpdsFeed CreateFeed(OpdsFeedKind kind, string id, string title, OpdsLinkBuilder links, string self, string? up)
    {
        var feed = new OpdsFeed
        {
            Kind = kind,
            Id = id,
            Title = title,
            Updated = timeProvider.GetUtcNow().UtcDateTime,
            Icon = links.Icon,
            Author = new OpdsAuthor { Name = CatalogTitle, Uri = ProjectUri },
        };

        feed.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Self, Href = self, Type = feed.MediaType });
        feed.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Start, Href = links.Root(), Type = OpdsMediaTypes.NavigationFeed, Title = CatalogTitle });
        if (up is not null)
        {
            feed.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Up, Href = up, Type = OpdsMediaTypes.NavigationFeed });
        }

        feed.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Search, Href = links.OpenSearchDescription(), Type = OpdsMediaTypes.OpenSearchDescription, Title = "Search" });
        feed.Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Search, Href = links.SearchTemplate(), Type = OpdsMediaTypes.Atom, Title = "Search" });
        return feed;
    }

    /// <summary>Concrete shelf ids (never "all"), so every query can filter with one <c>Contains</c>.</summary>
    private async Task<List<int>> GetVisibleShelfIdsAsync(CancellationToken cancellationToken)
    {
        var accessible = await shelfAccessService.GetAccessibleShelfIdsAsync(cancellationToken);
        if (accessible is not null)
        {
            return [.. accessible];
        }

        var all = await shelfRepository.FindAsync(new SearchOptions<Shelf> { CancellationToken = cancellationToken }, s => s.Id);
        return [.. all];
    }

    private (int Page, int PageSize) NormalisePaging(int page)
    {
        int pageSize = options.Value.EffectivePageSize;

        // Keeps the repository's (page - 1) * pageSize skip from overflowing on absurd page numbers.
        int maxPage = int.MaxValue / pageSize;
        return (Math.Clamp(page, 1, maxPage), pageSize);
    }

    private sealed record AuthorRef(int Id, string Name);

    private sealed record AuthorRow(int Id, string Name, string? PrimaryAuthorName, List<string> PseudonymNames, int BookCount);

    private sealed record BookRow(
        int Id,
        string Title,
        string? Description,
        string? Language,
        string? Publisher,
        string? Isbn,
        DateTime? PublishedOn,
        EbookFormat FileFormat,
        long FileSizeBytes,
        string FilePath,
        string? CoverImagePath,
        DateTime CreatedAt,
        DateTime? UpdatedAt,
        int? SeriesId,
        string? SeriesName,
        decimal? NumberInSeries,
        List<AuthorRef> Authors,
        List<string> Genres,
        List<string> Tags);
}
