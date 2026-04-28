namespace Shelfwarden.Services;

public class AuthorService(
    IRepository<Author> authorRepository,
    IRepository<Book> bookRepository,
    IRepository<BookAuthor> bookAuthorRepository,
    IRepository<BookProgress> progressRepository,
    IUserContextService userContext) : IAuthorService
{
    public async Task<Result<IReadOnlyList<AuthorDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Author>
        {
            PageNumber = 1,
            PageSize = Math.Clamp(limit, 1, 200),
            OrderBy = q => q.OrderBy(a => a.NormalizedName),
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = a => a.NormalizedName.Contains(needle);
        }

        var authors = await authorRepository.FindAsync(options);
        IReadOnlyList<AuthorDto> result = authors
            .Select(a => new AuthorDto(a.Id, a.Name, a.Biography))
            .ToList();
        return Result.Success(result);
    }

    public async Task<Result<AuthorDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == id,
        });
        return author is null ? (Result<AuthorDto>)Result.NotFound() : Result.Success(new AuthorDto(author.Id, author.Name, author.Biography));
    }

    public async Task<Result<AuthorDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Invalid(new ValidationError(nameof(name), "Name is required."));
        }

        string trimmed = name.Trim();
        string normalised = trimmed.ToLowerInvariant();

        var existing = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.NormalizedName == normalised,
        });
        if (existing is not null)
        {
            return Result.Success(new AuthorDto(existing.Id, existing.Name, existing.Biography));
        }

        var created = await authorRepository.InsertAsync(new Author
        {
            Name = trimmed,
            NormalizedName = normalised,
        });
        return Result.Success(new AuthorDto(created.Id, created.Name, created.Biography));
    }

    public async Task<Result<IReadOnlyList<AuthorListItemDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Author>
        {
            OrderBy = q => q.OrderBy(a => a.NormalizedName),
            CancellationToken = cancellationToken,
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = a => EF.Functions.Like(a.NormalizedName, $"%{needle}%");
        }

        var authors = (await authorRepository.FindAsync(options)).ToList();
        var ids = authors.Select(a => a.Id).ToList();

        // One join-table query → counts by author id. Cheaper than N round-trips.
        var counts = (await bookAuthorRepository.FindAsync(
                new SearchOptions<BookAuthor> { Query = ba => ids.Contains(ba.AuthorId) },
                ba => ba.AuthorId))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        IReadOnlyList<AuthorListItemDto> result = authors
            .Select(a => new AuthorListItemDto(a.Id, a.Name, a.Biography, counts.GetValueOrDefault(a.Id, 0)))
            .Where(a => a.BookCount > 0)
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<AuthorDetailDto>> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == id,
            CancellationToken = cancellationToken,
        });
        if (author is null)
        {
            return Result.NotFound($"Author {id} not found.");
        }

        // Load every book for the author with everything we need to build BookListItemDto
        // entries. The author can have at most a few hundred books in any realistic scenario,
        // so a single Include is fine.
        var books = (await bookRepository.FindAsync(new SearchOptions<Book>
        {
            Query = b => b.BookAuthors.Any(ba => ba.AuthorId == id),
            Include = q => q
                .Include(b => b.Series)
                .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
            OrderBy = q => q
                .OrderBy(b => b.SeriesId == null ? 1 : 0)
                .ThenBy(b => b.NumberInSeries)
                .ThenBy(b => b.SortTitle ?? b.Title),
            SplitQuery = true,
            CancellationToken = cancellationToken,
        })).ToList();

        // Per-user progress so the in-page cards can render the green progress bar.
        string? userId = userContext.GetCurrentUserId();
        var progressByBook = await BookProjections.LoadProgressPercentagesAsync(
            progressRepository, userId, books.Select(b => b.Id).ToList(), cancellationToken);

        var seriesGroups = books
            .Where(b => b.SeriesId.HasValue && b.Series is not null)
            .GroupBy(b => (b.SeriesId!.Value, b.Series!.Name))
            .Select(g => new AuthorSeriesGroupDto(
                g.Key.Value,
                g.Key.Name,
                g.Count(),
                g.OrderBy(b => b.NumberInSeries)
                    .Take(4)
                    .Select(b => new SeriesCoverDto(b.Id, b.CoverImagePath))
                    .ToList()))
            .OrderBy(s => s.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var standalone = books
            .Where(b => !b.SeriesId.HasValue)
            .Select(b => BookProjections.ToListItem(b, progressByBook.GetValueOrDefault(b.Id)))
            .ToList();

        return Result.Success(new AuthorDetailDto(
            author.Id,
            author.Name,
            author.Biography,
            books.Count,
            seriesGroups,
            standalone));
    }

    public async Task<Result<AuthorDto>> UpdateBiographyAsync(int id, string? biography, CancellationToken cancellationToken = default)
    {
        var author = await authorRepository.FindOneAsync(new SearchOptions<Author>
        {
            Query = a => a.Id == id,
            CancellationToken = cancellationToken,
        });
        if (author is null)
        {
            return Result.NotFound($"Author {id} not found.");
        }

        author.Biography = string.IsNullOrWhiteSpace(biography) ? null : biography.Trim();
        await authorRepository.UpdateAsync(author);

        return Result.Success(new AuthorDto(author.Id, author.Name, author.Biography));
    }
}