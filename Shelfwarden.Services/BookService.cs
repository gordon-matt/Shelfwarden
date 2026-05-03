using LinqKit;
using Microsoft.EntityFrameworkCore;
using Shelfwarden.Data.Entities;

namespace Shelfwarden.Services;

public class BookService(
    ILogger<BookService> logger,
    IUserContextService userContext,
    IShelfAccessService shelfAccessService,
    IRepository<Book> bookRepository,
    IRepository<BookAuthor> bookAuthorRepository,
    IRepository<BookGenre> bookGenreRepository,
    IRepository<Tag> tagRepository,
    IRepository<BookTag> bookTagRepository,
    IRepository<BookProgress> progressRepository) : IBookService
{
    public async Task<Result<PagedList<BookListItemDto>>> SearchAsync(BookSearchRequest request, CancellationToken cancellationToken = default)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 200);

        var options = new SearchOptions<Book>
        {
            PageNumber = page,
            PageSize = pageSize,
            Include = q => q
                .Include(b => b.Series)
                .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
            SplitQuery = true,
        };

        var predicate = PredicateBuilder.New<Book>(true);

        IReadOnlySet<int>? accessibleShelves = await shelfAccessService.GetAccessibleShelfIdsAsync(cancellationToken);
        if (accessibleShelves is not null)
        {
            if (accessibleShelves.Count == 0)
            {
                return Result.Success(new PagedList<BookListItemDto>([], 0, page, pageSize));
            }

            if (request.ShelfId is int sid)
            {
                if (!accessibleShelves.Contains(sid))
                {
                    return Result.Success(new PagedList<BookListItemDto>([], 0, page, pageSize));
                }

                predicate = predicate.And(b => b.ShelfId == sid);
            }
            else
            {
                predicate = predicate.And(b => accessibleShelves.Contains(b.ShelfId));
            }
        }
        else if (request.ShelfId is int sid)
        {
            predicate = predicate.And(b => b.ShelfId == sid);
        }

        if (request.SeriesId is int seriesFilter)
        {
            if (seriesFilter == -1)
            {
                predicate = predicate.And(b => b.SeriesId == null);
            }
            else if (seriesFilter > 0)
            {
                predicate = predicate.And(b => b.SeriesId == seriesFilter);
            }
        }

        if (request.AuthorId is int aId)
        {
            predicate = predicate.And(b => b.BookAuthors.Any(ba => ba.AuthorId == aId));
        }

        if (request.GenreId is int genreFilter)
        {
            if (genreFilter == -1)
            {
                predicate = predicate.And(b => !b.BookGenres.Any());
            }
            else if (genreFilter > 0)
            {
                predicate = predicate.And(b => b.BookGenres.Any(bg => bg.GenreId == genreFilter));
            }
        }

        if (request.CollectionId is int collectionFilter)
        {
            if (collectionFilter == -1)
            {
                predicate = predicate.And(b => !b.CollectionBooks.Any());
            }
            else if (collectionFilter > 0)
            {
                predicate = predicate.And(b => b.CollectionBooks.Any(l => l.CollectionId == collectionFilter));
            }
        }

        if (request.AwaitingReview)
        {
            predicate = predicate.And(b => b.UpdatedAt == null);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            string q = request.Query.Trim().ToUpperInvariant();
            predicate = predicate.And(b => b.Title.ToUpper().Contains(q));
        }

        if (!string.IsNullOrWhiteSpace(request.StartsWith))
        {
            string startsWith = request.StartsWith.Trim();
            if (startsWith == "#")
            {
                predicate = predicate.And(b =>
                    b.Title.Length > 0 &&
                    !((b.Title[0] >= 'A' && b.Title[0] <= 'Z') || (b.Title[0] >= 'a' && b.Title[0] <= 'z')));
            }
            else if (startsWith.Length == 1 && char.IsLetter(startsWith[0]))
            {
                char upper = char.ToUpperInvariant(startsWith[0]);
                string lower = char.ToLowerInvariant(upper).ToString();
                string upperText = upper.ToString();
                predicate = predicate.And(b => b.Title.StartsWith(upperText) || b.Title.StartsWith(lower));
            }
        }

        options.Query = predicate;

        options.OrderBy = request.SortBy switch
        {
            BookSortBy.Title => request.SortDescending
                ? q => q.OrderByDescending(b => b.SortTitle ?? b.Title)
                : q => q.OrderBy(b => b.SortTitle ?? b.Title),
            BookSortBy.AddedAt => request.SortDescending
                ? q => q.OrderByDescending(b => b.CreatedAt)
                : q => q.OrderBy(b => b.CreatedAt),
            BookSortBy.PublishedOn => request.SortDescending
                ? q => q.OrderByDescending(b => b.PublishedOn)
                : q => q.OrderBy(b => b.PublishedOn),
            BookSortBy.NumberInSeries => request.SortDescending
                ? q => q.OrderByDescending(b => b.SeriesId).ThenByDescending(b => b.NumberInSeries)
                : q => q.OrderBy(b => b.SeriesId).ThenBy(b => b.NumberInSeries),
            BookSortBy.UpdatedAt => request.SortDescending
                ? q => q.OrderByDescending(b => b.UpdatedAt)
                : q => q.OrderBy(b => b.UpdatedAt),
            _ => q => q.OrderBy(b => b.SortTitle ?? b.Title),
        };

        var page_ = await bookRepository.FindAsync(options);
        string? userId = userContext.GetCurrentUserId();

        var bookIds = page_.Select(b => b.Id).ToList();
        var progressByBook = await LoadProgressMapAsync(userId, bookIds);

        var items = page_
            .Select(b => BookProjections.ToListItem(
                b,
                progressByBook.GetValueOrDefault(b.Id)?.Percentage ?? 0))
            .ToList();

        var result = new PagedList<BookListItemDto>(items, page_.ItemCount, page, pageSize);
        return Result.Success(result);
    }

    public async Task<Result<BookDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == id,
            Include = q => q
                .Include(b => b.Shelf)
                    .ThenInclude(s => s.UserAccessEntries)
                .Include(b => b.Shelf)
                    .ThenInclude(s => s.RoleAccessEntries)
                .Include(b => b.Series)
                .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author)
                .Include(b => b.BookGenres).ThenInclude(bg => bg.Genre)
                .Include(b => b.BookTags).ThenInclude(bt => bt.Tag),
            SplitQuery = true,
            CancellationToken = cancellationToken,
        });

        if (book is null)
        {
            return Result.NotFound($"Book {id} not found.");
        }

        if (!ShelfAccessEvaluator.CanAccessShelf(book.Shelf, userContext))
        {
            return Result.NotFound($"Book {id} not found.");
        }

        return Result.Success(MapBook(book));
    }

    public async Task<Result<BookDto>> UpdateAsync(int id, UpdateBookRequest request, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == id,
            Include = q => q
                .Include(b => b.BookAuthors)
                .Include(b => b.BookGenres)
                .Include(b => b.BookTags),
            SplitQuery = true,
        });

        if (book is null)
        {
            return Result.NotFound($"Book {id} not found.");
        }

        book.Title = request.Title.Trim();
        book.SortTitle = NullIfWhitespace(request.SortTitle);
        book.Subtitle = NullIfWhitespace(request.Subtitle);
        book.Description = request.Description;
        book.Language = NullIfWhitespace(request.Language);
        book.Publisher = NullIfWhitespace(request.Publisher);
        book.Isbn = NullIfWhitespace(request.Isbn);
        book.PublishedOn = request.PublishedOn;
        book.SeriesId = request.SeriesId;
        book.NumberInSeries = request.NumberInSeries;
        book.UpdatedAt = DateTime.UtcNow;

        await bookRepository.UpdateAsync(book);

        await SyncBookAuthorsAsync(id, request.AuthorIds);
        await SyncBookGenresAsync(id, request.GenreIds);
        await SyncBookTagsAsync(id, request.Tags);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == id,
        });

        if (book is null)
        {
            return Result.NotFound();
        }

        await bookRepository.DeleteAsync(book);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Deleted book {BookId} ({Title})", id, book.Title);
        }

        return Result.Success();
    }

    public async Task<Result<BookProgressDto?>> GetProgressAsync(int id, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result<BookProgressDto?>.Unauthorized();
        }

        var existing = await progressRepository.FindOneAsync(new SearchOptions<BookProgress>
        {
            Query = p => p.BookId == id && p.UserId == userId,
            CancellationToken = cancellationToken,
        });

        var dto = existing is null
            ? null
            : new BookProgressDto(existing.BookId, existing.Percentage, existing.PageNumber, existing.Location, existing.LastReadAt);

        return Result<BookProgressDto?>.Success(dto);
    }

    public async Task<Result<BookProgressDto>> SaveProgressAsync(int id, SaveProgressRequest request, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book> { Query = b => b.Id == id });
        if (book is null)
        {
            return Result.NotFound();
        }

        var existing = await progressRepository.FindOneAsync(new SearchOptions<BookProgress>
        {
            Query = p => p.BookId == id && p.UserId == userId,
        });

        BookProgress saved;
        if (existing is null)
        {
            saved = await progressRepository.InsertAsync(new BookProgress
            {
                BookId = id,
                UserId = userId,
                Percentage = request.Percentage,
                PageNumber = request.PageNumber,
                Location = request.Location,
                LastReadAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Percentage = request.Percentage;
            existing.PageNumber = request.PageNumber;
            existing.Location = request.Location;
            existing.LastReadAt = DateTime.UtcNow;
            saved = await progressRepository.UpdateAsync(existing);
        }

        return Result.Success(new BookProgressDto(
            saved.BookId, saved.Percentage, saved.PageNumber, saved.Location, saved.LastReadAt));
    }

    private async Task<Dictionary<int, BookProgress>> LoadProgressMapAsync(string? userId, IReadOnlyList<int> bookIds)
    {
        if (string.IsNullOrEmpty(userId) || bookIds.Count == 0)
        {
            return [];
        }

        var rows = await progressRepository.FindAsync(new SearchOptions<BookProgress>
        {
            Query = p => p.UserId == userId && bookIds.Contains(p.BookId),
        });
        return rows.ToDictionary(p => p.BookId);
    }

    private async Task SyncBookAuthorsAsync(int bookId, IReadOnlyList<int> authorIds)
    {
        var existing = (await bookAuthorRepository.FindAsync(new SearchOptions<BookAuthor>
        {
            Query = ba => ba.BookId == bookId,
        })).ToList();

        var existingIds = existing.Select(ba => ba.AuthorId).ToHashSet();
        var desiredIds = authorIds.Distinct().ToHashSet();

        var toRemove = existing.Where(ba => !desiredIds.Contains(ba.AuthorId)).ToList();
        if (toRemove.Count > 0)
        {
            await bookAuthorRepository.DeleteAsync(toRemove);
        }

        var toAdd = desiredIds
            .Where(aid => !existingIds.Contains(aid))
            .Select((aid, idx) => new BookAuthor { BookId = bookId, AuthorId = aid, Position = idx })
            .ToList();

        if (toAdd.Count > 0)
        {
            await bookAuthorRepository.InsertAsync(toAdd);
        }
    }

    private async Task SyncBookGenresAsync(int bookId, IReadOnlyList<int> genreIds)
    {
        var existing = (await bookGenreRepository.FindAsync(new SearchOptions<BookGenre>
        {
            Query = bg => bg.BookId == bookId,
        })).ToList();

        var existingIds = existing.Select(bg => bg.GenreId).ToHashSet();
        var desiredIds = genreIds.Distinct().ToHashSet();

        var toRemove = existing.Where(bg => !desiredIds.Contains(bg.GenreId)).ToList();
        if (toRemove.Count > 0)
        {
            await bookGenreRepository.DeleteAsync(toRemove);
        }

        var toAdd = desiredIds
            .Where(gid => !existingIds.Contains(gid))
            .Select(gid => new BookGenre { BookId = bookId, GenreId = gid })
            .ToList();
        if (toAdd.Count > 0)
        {
            await bookGenreRepository.InsertAsync(toAdd);
        }
    }

    private async Task SyncBookTagsAsync(int bookId, IReadOnlyList<string> tagNames)
    {
        var normalised = tagNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Resolve / create tags by normalised name.
        var lookup = normalised.Select(n => n.ToLowerInvariant()).ToList();
        var existingTags = (await tagRepository.FindAsync(new SearchOptions<Tag>
        {
            Query = t => lookup.Contains(t.NormalizedName),
        })).ToList();

        var byNormalized = existingTags.ToDictionary(t => t.NormalizedName, StringComparer.OrdinalIgnoreCase);

        var toCreate = new List<Tag>();
        foreach (string? name in normalised)
        {
            if (!byNormalized.ContainsKey(name.ToLowerInvariant()))
            {
                toCreate.Add(new Tag { Name = name, NormalizedName = name.ToLowerInvariant() });
            }
        }
        if (toCreate.Count > 0)
        {
            var created = await tagRepository.InsertAsync(toCreate);
            foreach (var t in created)
            {
                byNormalized[t.NormalizedName] = t;
            }
        }

        // Sync the join table.
        var existingJoins = (await bookTagRepository.FindAsync(new SearchOptions<BookTag>
        {
            Query = bt => bt.BookId == bookId,
        })).ToList();

        var desiredTagIds = normalised
            .Select(n => byNormalized[n.ToLowerInvariant()].Id)
            .ToHashSet();

        var toRemove = existingJoins.Where(bt => !desiredTagIds.Contains(bt.TagId)).ToList();
        if (toRemove.Count > 0)
        {
            await bookTagRepository.DeleteAsync(toRemove);
        }

        var existingJoinIds = existingJoins.Select(bt => bt.TagId).ToHashSet();
        var toAddJoins = desiredTagIds
            .Where(tid => !existingJoinIds.Contains(tid))
            .Select(tid => new BookTag { BookId = bookId, TagId = tid })
            .ToList();
        if (toAddJoins.Count > 0)
        {
            await bookTagRepository.InsertAsync(toAddJoins);
        }
    }

    private static string? NullIfWhitespace(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static BookDto MapBook(Book b) => new(
        b.Id,
        b.Title,
        b.SortTitle,
        b.Subtitle,
        b.Description,
        b.Language,
        b.Publisher,
        b.Isbn,
        b.PublishedOn,
        b.PageCount,
        b.FilePath,
        b.FileSizeBytes,
        b.FileFormat,
        b.CoverImagePath,
        b.ShelfId,
        b.Series is null ? null : new SeriesDto(b.Series.Id, b.Series.Name, b.Series.Description, BookCount: 0),
        b.NumberInSeries,
        b.BookAuthors
            .OrderBy(ba => ba.Position)
            .Select(ba => new AuthorDto(ba.Author.Id, ba.Author.Name, ba.Author.Biography))
            .ToList(),
        b.BookGenres
            .Select(bg => new GenreDto(bg.Genre.Id, bg.Genre.Name))
            .ToList(),
        b.BookTags
            .Select(bt => bt.Tag.Name)
            .ToList(),
        b.CreatedAt,
        b.LastScannedAt,
        b.UpdatedAt);
}