namespace Shelfwarden.Services;

public class BookUserService(
    IUserContextService userContext,
    IRepository<Book> bookRepository,
    IRepository<BookUser> bookUserRepository) : IBookUserService
{
    public async Task<Result<BookUserDto>> GetAsync(int bookId, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        var row = await FindRowAsync(bookId, userId, cancellationToken);
        return Result.Success(new BookUserDto(bookId, row?.Rating, row?.Notes));
    }

    public async Task<Result<int?>> SetRatingAsync(int bookId, int? rating, CancellationToken cancellationToken = default)
    {
        if (rating is < 0 or > 5)
        {
            return Result.Invalid(new ValidationError(nameof(rating), "Rating must be between 0 and 5."));
        }

        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        if (!await bookRepository.ExistsAsync(b => b.Id == bookId))
        {
            return Result.NotFound($"Book {bookId} not found.");
        }

        byte? value = rating is > 0 ? (byte)rating.Value : null;
        var row = await FindRowAsync(bookId, userId, cancellationToken);
        await UpsertAsync(bookId, userId, row, bu => bu.Rating = value);
        return Result.Success((int?)value);
    }

    public async Task<Result> SetNotesAsync(int bookId, string? notes, CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Result.Unauthorized();
        }

        if (!await bookRepository.ExistsAsync(b => b.Id == bookId))
        {
            return Result.NotFound($"Book {bookId} not found.");
        }

        string? value = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        var row = await FindRowAsync(bookId, userId, cancellationToken);
        await UpsertAsync(bookId, userId, row, bu => bu.Notes = value);
        return Result.Success();
    }

    private Task<BookUser?> FindRowAsync(int bookId, string userId, CancellationToken cancellationToken)
        => bookUserRepository.FindOneAsync(new SearchOptions<BookUser>
        {
            Query = bu => bu.BookId == bookId && bu.UserId == userId,
            CancellationToken = cancellationToken,
        });

    private async Task UpsertAsync(int bookId, string userId, BookUser? existing, Action<BookUser> apply)
    {
        if (existing is null)
        {
            var row = new BookUser { BookId = bookId, UserId = userId };
            apply(row);
            await bookUserRepository.InsertAsync(row);
        }
        else
        {
            apply(existing);
            await bookUserRepository.UpdateAsync(existing);
        }
    }
}
