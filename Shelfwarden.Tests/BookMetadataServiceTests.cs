using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shelfwarden.Models.Metadata;
using Shelfwarden.Services.Auth;
using Shelfwarden.Services.Metadata;

namespace Shelfwarden.Tests;

public class BookMetadataServiceTests
{
    [Fact]
    public async Task FindBestMatch_prefers_isbn_match_over_other_results()
    {
        var wrong = Candidate("Provider A", title: "Wrong Book", isbn: "9990000000000");
        var right = Candidate("Provider B", title: "Right Book", isbn: "1110000000000");

        var service = BuildService(
            new FakeProvider("Provider A", priority: 0, [wrong]),
            new FakeProvider("Provider B", priority: 1, [right]));

        var result = await service.FindBestMatchAsync(new BookMetadataQuery { Title = "Book", Isbn = "111-0000000000" });

        Assert.NotNull(result);
        Assert.Equal("Right Book", result!.Title);
        Assert.Equal("1110000000000", result.Isbn);
    }

    [Fact]
    public async Task FindBestMatch_backfills_null_fields_from_lower_ranked_results()
    {
        // Top result wins on ISBN but lacks a description; a lower-ranked result supplies one.
        var top = Candidate("Provider A", title: "The Book", isbn: "1110000000000", description: null);
        var other = Candidate("Provider B", title: "The Book", isbn: null, description: "A great read.");

        var service = BuildService(
            new FakeProvider("Provider A", priority: 0, [top]),
            new FakeProvider("Provider B", priority: 1, [other]));

        var result = await service.FindBestMatchAsync(new BookMetadataQuery { Title = "The Book", Isbn = "1110000000000" });

        Assert.NotNull(result);
        Assert.Equal("The Book", result!.Title);
        Assert.Equal("A great read.", result.Description);
    }

    [Fact]
    public async Task FindBestMatch_returns_null_when_no_provider_matches()
    {
        var service = BuildService(new FakeProvider("Provider A", priority: 0, []));

        var result = await service.FindBestMatchAsync(new BookMetadataQuery { Title = "Nothing" });

        Assert.Null(result);
    }

    [Fact]
    public async Task FindBestMatch_returns_null_for_empty_query()
    {
        var service = BuildService(new FakeProvider("Provider A", priority: 0, [Candidate("Provider A", "X")]));

        var result = await service.FindBestMatchAsync(new BookMetadataQuery());

        Assert.Null(result);
    }

    [Fact]
    public async Task FindBestMatch_survives_a_throwing_provider()
    {
        var good = Candidate("Provider B", title: "Survivor", isbn: "1110000000000");
        var service = BuildService(
            new ThrowingProvider("Provider A"),
            new FakeProvider("Provider B", priority: 1, [good]));

        var result = await service.FindBestMatchAsync(new BookMetadataQuery { Title = "Survivor" });

        Assert.NotNull(result);
        Assert.Equal("Survivor", result!.Title);
    }

    private static BookMetadataService BuildService(params IBookMetadataProvider[] providers)
    {
        var userContext = new Mock<IUserContextService>();
        return new BookMetadataService(NullLogger<BookMetadataService>.Instance, userContext.Object, providers);
    }

    private static ExternalBookMetadataDto Candidate(
        string provider,
        string title,
        string? isbn = null,
        string? description = null)
        => new(
            Provider: provider,
            ProviderId: null,
            Title: title,
            Subtitle: null,
            Description: description,
            Language: null,
            Publisher: null,
            Isbn: isbn,
            PublishedOn: null,
            PageCount: null,
            SeriesName: null,
            NumberInSeries: null,
            Authors: [],
            Genres: [],
            Tags: [],
            CoverUrl: null,
            InfoUrl: null);

    private sealed class FakeProvider(string name, int priority, IReadOnlyList<ExternalBookMetadataDto> results) : IBookMetadataProvider
    {
        public string Name => name;

        public int Priority => priority;

        public Task<IReadOnlyList<ExternalBookMetadataDto>> SearchAsync(BookMetadataQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(results);
    }

    private sealed class ThrowingProvider(string name) : IBookMetadataProvider
    {
        public string Name => name;

        public int Priority => 0;

        public Task<IReadOnlyList<ExternalBookMetadataDto>> SearchAsync(BookMetadataQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }
}
