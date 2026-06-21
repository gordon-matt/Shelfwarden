using LinqKit;

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
            Include = query => query
                .Include(b => b.Series)
                .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
            SplitQuery = true,
        };

        var predicate = PredicateBuilder.New<Book>(true);

        var accessibleShelves = await shelfAccessService.GetAccessibleShelfIdsAsync(cancellationToken);
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

        if (request.AuthorId is int authorFilter)
        {
            if (authorFilter == -1)
            {
                predicate = predicate.And(b => !b.BookAuthors.Any());
            }
            else if (authorFilter > 0)
            {
                predicate = predicate.And(b => b.BookAuthors.Any(ba => ba.AuthorId == authorFilter));
            }
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

        if (request.ReadingListId is int listId && listId > 0)
        {
            predicate = predicate.And(b => b.ReadingListItems.Any(i => i.ReadingListId == listId));
        }

        var requestedTagIds = request.TagIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (requestedTagIds.Count > 0)
        {
            predicate = predicate.And(b =>
                b.BookTags
                    .Where(bt => requestedTagIds.Contains(bt.TagId))
                    .Select(bt => bt.TagId)
                    .Distinct()
                    .Count() == requestedTagIds.Count);
        }
        else if (request.TagId is int tagFilter)
        {
            if (tagFilter == -1)
            {
                predicate = predicate.And(b => !b.BookTags.Any());
            }
            else if (tagFilter > 0)
            {
                predicate = predicate.And(b => b.BookTags.Any(bt => bt.TagId == tagFilter));
            }
        }

        if (request.AwaitingReview)
        {
            predicate = predicate.And(b => b.UpdatedAt == null);
        }

        // Read-status filter is per-user; quietly degrades to "no filter" for anonymous calls
        // (e.g. a future API client without a session) so we don't accidentally return zero
        // results when the caller has no identity to resolve "Read" against.
        if (request.ReadStatus != BookReadStatusFilter.Any)
        {
            string? readStatusUserId = userContext.GetCurrentUserId();
            if (!string.IsNullOrEmpty(readStatusUserId))
            {
                double threshold = Constants.FinishedThresholdPercent;
                if (request.ReadStatus == BookReadStatusFilter.Read)
                {
                    predicate = predicate.And(b => b.ReadingProgress.Any(
                        p => p.UserId == readStatusUserId && p.Percentage >= threshold));
                }
                else
                {
                    // Unread = no progress row, OR progress below the finished threshold.
                    // `!Any(p => …finished)` covers both cases in a single sub-query.
                    predicate = predicate.And(b => !b.ReadingProgress.Any(
                        p => p.UserId == readStatusUserId && p.Percentage >= threshold));
                }
            }
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
                (
                    !string.IsNullOrEmpty(b.Title) &&
                    !Constants.Letters.Any(l => b.Title.StartsWith(l))
                ) ||
                (
                    !string.IsNullOrEmpty(b.SortTitle) &&
                    !Constants.Letters.Any(l => b.SortTitle.StartsWith(l))
                ));
            }
            else if (startsWith.Length == 1 && char.IsLetter(startsWith[0]))
            {
                char upper = char.ToUpperInvariant(startsWith[0]);
                string lower = char.ToLowerInvariant(upper).ToString();
                string upperText = upper.ToString();
                predicate = predicate.And(b =>
                (
                    !string.IsNullOrEmpty(b.Title) &&
                    (
                        b.Title.StartsWith(upperText) ||
                        b.Title.StartsWith(lower)
                    )
                ) ||
                (
                    !string.IsNullOrEmpty(b.SortTitle) &&
                    (
                        b.SortTitle.StartsWith(upperText) ||
                        b.SortTitle.StartsWith(lower)
                    )
                ));
            }
        }

        options.Query = predicate;

        options.OrderBy = request.SortBy switch
        {
            BookSortBy.Title => request.SortDescending
                ? query => query.OrderByDescending(b => b.SortTitle ?? b.Title).ThenByDescending(b => b.Id)
                : query => query.OrderBy(b => b.SortTitle ?? b.Title).ThenBy(b => b.Id),
            BookSortBy.AddedAt => request.SortDescending
                ? query => query.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
                : query => query.OrderBy(b => b.CreatedAt).ThenBy(b => b.Id),
            BookSortBy.PublishedOn => request.SortDescending
                ? query => query.OrderByDescending(b => b.PublishedOn).ThenByDescending(b => b.Id)
                : query => query.OrderBy(b => b.PublishedOn).ThenBy(b => b.Id),
            BookSortBy.NumberInSeries => request.SortDescending
                ? query => query.OrderByDescending(b => b.SeriesId).ThenByDescending(b => b.NumberInSeries).ThenByDescending(b => b.Id)
                : query => query.OrderBy(b => b.SeriesId).ThenBy(b => b.NumberInSeries).ThenBy(b => b.Id),
            BookSortBy.UpdatedAt => request.SortDescending
                ? query => query.OrderByDescending(b => b.UpdatedAt).ThenByDescending(b => b.Id)
                : query => query.OrderBy(b => b.UpdatedAt).ThenBy(b => b.Id),
            _ => query => query.OrderBy(b => b.SortTitle ?? b.Title).ThenBy(b => b.Id),
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

    public async Task<Result<IReadOnlyList<BookListItemDto>>> GetListItemsByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Where(i => i > 0).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<BookListItemDto>>([]);
        }

        var accessibleShelves = await shelfAccessService.GetAccessibleShelfIdsAsync(cancellationToken);

        var query = new SearchOptions<Book>
        {
            Query = accessibleShelves is null
                ? b => distinctIds.Contains(b.Id)
                : b => distinctIds.Contains(b.Id) && accessibleShelves.Contains(b.ShelfId),
            Include = query => query
                .Include(b => b.Series)
                .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
            SplitQuery = true,
            CancellationToken = cancellationToken,
        };

        var books = (await bookRepository.FindAsync(query)).ToList();
        if (books.Count == 0)
        {
            return Result.Success<IReadOnlyList<BookListItemDto>>([]);
        }

        string? userId = userContext.GetCurrentUserId();
        var progress = await BookProjections.LoadProgressPercentagesAsync(
            progressRepository, userId, books.Select(b => b.Id).ToList(), cancellationToken);

        IReadOnlyList<BookListItemDto> items = books
            .Select(b => BookProjections.ToListItem(b, progress.GetValueOrDefault(b.Id, 0)))
            .ToList();
        return Result.Success(items);
    }

    public async Task<Result<BookDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == id,
            Include = query => query
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

        return book is null
            ? (Result<BookDto>)Result.NotFound($"Book {id} not found.")
            : !ShelfAccessEvaluator.CanAccessShelf(book.Shelf, userContext)
            ? (Result<BookDto>)Result.NotFound($"Book {id} not found.")
            : Result.Success(MapBook(book));
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
            Include = query => query
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

    public async Task<Result<BookProgressDto>> MarkAsReadAsync(int id, CancellationToken cancellationToken = default)
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

        // Pin to 100% and preserve the existing locator/page so the reader still knows where they
        // were if they later "unread" by re-opening the book.
        BookProgress saved;
        if (existing is null)
        {
            saved = await progressRepository.InsertAsync(new BookProgress
            {
                BookId = id,
                UserId = userId,
                Percentage = 100d,
                LastReadAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Percentage = 100d;
            existing.LastReadAt = DateTime.UtcNow;
            saved = await progressRepository.UpdateAsync(existing);
        }

        return Result.Success(new BookProgressDto(
            saved.BookId, saved.Percentage, saved.PageNumber, saved.Location, saved.LastReadAt));
    }

    public async Task<Result> MarkAsUnreadAsync(int id, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var existing = await progressRepository.FindOneAsync(new SearchOptions<BookProgress>
        {
            Query = p => p.BookId == id && p.UserId == userId,
        });

        if (existing is not null)
        {
            await progressRepository.DeleteAsync(existing);
        }

        return Result.Success();
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
            .OrderBy(bt => bt.Tag.NormalizedName)
            .Select(bt => new TagDto(bt.Tag.Id, bt.Tag.Name))
            .ToList(),
        b.CreatedAt,
        b.LastScannedAt,
        b.UpdatedAt);
}