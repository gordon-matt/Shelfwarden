using Microsoft.Extensions.DependencyInjection;
using Shelfwarden.Tests.Infrastructure;

namespace Shelfwarden.Tests;

/// <summary>
/// Smoke test that confirms the entity graph + repositories wire up correctly under the
/// in-memory fixture. Real provider-specific behaviour (indexes, JSON columns, SQL Server
/// max-length, etc.) is exercised in integration tests, not here.
/// </summary>
public class BookEntityTests : IClassFixture<InMemoryDbFixture>
{
    private readonly InMemoryDbFixture _fixture;

    public BookEntityTests(InMemoryDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InsertedBookCanBeRetrievedThroughRepository()
    {
        using var scope = _fixture.CreateScope();
        var shelves = scope.ServiceProvider.GetRequiredService<IRepository<Shelf>>();
        var books = scope.ServiceProvider.GetRequiredService<IRepository<Book>>();

        var shelf = await shelves.InsertAsync(new Shelf
        {
            Name = "Test Shelf",
            CreatedAt = DateTime.UtcNow,
        });

        await books.InsertAsync(new Book
        {
            ShelfId = shelf.Id,
            Title = "The Hobbit",
            FilePath = @"C:\Books\hobbit.epub",
            FileFormat = EbookFormat.Epub,
            FileSizeBytes = 1024,
            CreatedAt = DateTime.UtcNow,
        });

        var found = await books.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Title == "The Hobbit",
        });

        Assert.NotNull(found);
        Assert.Equal(shelf.Id, found.ShelfId);
        Assert.Equal(EbookFormat.Epub, found.FileFormat);
    }
}