namespace Shelfwarden.Services;

public interface IBookService
{
    Task<Result<PagedList<BookListItemDto>>> SearchAsync(BookSearchRequest request, CancellationToken cancellationToken = default);

    Task<Result<BookDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<BookDto>> UpdateAsync(int id, UpdateBookRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<BookProgressDto>> SaveProgressAsync(int id, SaveProgressRequest request, CancellationToken cancellationToken = default);
}
