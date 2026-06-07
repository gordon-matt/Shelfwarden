namespace Shelfwarden.Services;

public class BookmarkService(
    IUserContextService userContext,
    IRepository<Book> bookRepository,
    IRepository<Bookmark> bookmarkRepository) : IBookmarkService
{
    public async Task<Result<IReadOnlyList<BookmarkDto>>> ListAsync(int bookId, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var rows = await bookmarkRepository.FindAsync(new SearchOptions<Bookmark>
        {
            Query = b => b.BookId == bookId && b.UserId == userId,
            OrderBy = query => query.OrderBy(b => b.CreatedAt),
            CancellationToken = cancellationToken,
        });

        IReadOnlyList<BookmarkDto> dto = rows.Select(Map).ToList();
        return Result.Success(dto);
    }

    public async Task<Result<BookmarkDto>> CreateAsync(int bookId, CreateBookmarkRequest request, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });
        if (book is null)
        {
            return Result.NotFound();
        }

        // Either a page number (PDF) or a CFI location (EPUB) must be supplied — otherwise
        // we have nowhere to jump back to. Belt-and-braces validation in case the reader
        // tries to bookmark too eagerly.
        if (!request.PageNumber.HasValue && string.IsNullOrWhiteSpace(request.Location))
        {
            return Result.Invalid(new ValidationError(nameof(request.Location),
                "Bookmark needs either a page number or a location."));
        }

        var inserted = await bookmarkRepository.InsertAsync(new Bookmark
        {
            UserId = userId,
            BookId = bookId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
            PageNumber = request.PageNumber,
            Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAt = DateTime.UtcNow,
        });

        return Result.Success(Map(inserted));
    }

    public async Task<Result<BookmarkDto>> UpdateAsync(int id, UpdateBookmarkRequest request, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var bookmark = await bookmarkRepository.FindOneAsync(new SearchOptions<Bookmark>
        {
            Query = b => b.Id == id,
            CancellationToken = cancellationToken,
        });
        if (bookmark is null)
        {
            return Result.NotFound();
        }
        if (bookmark.UserId != userId)
        {
            return Result.Forbidden();
        }

        bookmark.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        bookmark.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var updated = await bookmarkRepository.UpdateAsync(bookmark);
        return Result.Success(Map(updated));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var bookmark = await bookmarkRepository.FindOneAsync(new SearchOptions<Bookmark>
        {
            Query = b => b.Id == id,
            CancellationToken = cancellationToken,
        });

        if (bookmark is null)
        {
            return Result.NotFound();
        }

        if (bookmark.UserId != userId)
        {
            return Result.Forbidden();
        }

        await bookmarkRepository.DeleteAsync(bookmark);
        return Result.Success();
    }

    private static BookmarkDto Map(Bookmark b) => new(
        b.Id, b.BookId, b.Title, b.PageNumber, b.Location, b.Note, b.CreatedAt);
}