using Shelfwarden.Models.Metadata;

namespace Shelfwarden.Services.Metadata;

/// <summary>
/// Default <see cref="IBookMetadataService"/>: queries every registered provider in parallel, then
/// ranks candidates (ISBN match → title match → field completeness → provider priority) the way
/// Calibre's identify pipeline does, and can merge the top results into one best-effort record.
/// </summary>
public sealed class BookMetadataService(
    ILogger<BookMetadataService> logger,
    IUserContextService userContext,
    IEnumerable<IBookMetadataProvider> providers) : IBookMetadataService
{
    private readonly IReadOnlyList<IBookMetadataProvider> _providers = providers.ToList();

    public async Task<Result<IReadOnlyList<ExternalBookMetadataDto>>> SearchAsync(BookMetadataSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var query = new BookMetadataQuery
        {
            Title = request.Title,
            Author = request.Author,
            Isbn = request.Isbn,
            Limit = Math.Clamp(request.Limit, 1, 20),
        };

        if (query.IsEmpty)
        {
            return Result.Invalid(new ValidationError(nameof(request.Title), "Enter a title, author, or ISBN to search."));
        }

        try
        {
            var ranked = await GatherRankedAsync(query, cancellationToken);
            return Result.Success(ranked);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Online metadata search failed");
            return Result.Error("Could not search online metadata sources.");
        }
    }

    public async Task<ExternalBookMetadataDto?> FindBestMatchAsync(BookMetadataQuery query, CancellationToken cancellationToken = default)
    {
        if (query.IsEmpty)
        {
            return null;
        }

        try
        {
            var ranked = await GatherRankedAsync(query, cancellationToken);
            return ranked.Count == 0 ? null : Merge(ranked);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Online metadata best-match lookup failed for '{Title}' / ISBN '{Isbn}'", query.Title, query.Isbn);
            return null;
        }
    }

    private async Task<IReadOnlyList<ExternalBookMetadataDto>> GatherRankedAsync(BookMetadataQuery query, CancellationToken cancellationToken)
    {
        var tasks = _providers.Select(p => SafeSearchAsync(p, query, cancellationToken));
        var perProvider = await Task.WhenAll(tasks);

        var all = perProvider.SelectMany(r => r).ToList();
        if (all.Count == 0)
        {
            return [];
        }

        string? wantedIsbn = query.HasIsbn ? MetadataNormalization.NormalizeIsbn(query.Isbn!) : null;
        string normalizedTitle = NormalizeForCompare(query.Title);

        return all
            .Select(c => new { Candidate = c, Score = ScoreCandidate(c, wantedIsbn, normalizedTitle) })
            .OrderByDescending(x => x.Score.IsbnMatch)
            .ThenByDescending(x => x.Score.TitleScore)
            .ThenByDescending(x => x.Score.Completeness)
            .ThenBy(x => x.Score.ProviderPriority)
            .Select(x => x.Candidate)
            .ToList();
    }

    private async Task<IReadOnlyList<ExternalBookMetadataDto>> SafeSearchAsync(IBookMetadataProvider provider, BookMetadataQuery query, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.SearchAsync(query, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Metadata provider '{Provider}' threw during search", provider.Name);
            return [];
        }
    }

    private CandidateScore ScoreCandidate(ExternalBookMetadataDto c, string? wantedIsbn, string normalizedTitle)
    {
        bool isbnMatch = wantedIsbn is { Length: > 0 }
            && !string.IsNullOrWhiteSpace(c.Isbn)
            && MetadataNormalization.NormalizeIsbn(c.Isbn!) == wantedIsbn;

        int providerPriority = _providers
            .FirstOrDefault(p => p.Name == c.Provider)?.Priority ?? int.MaxValue;

        return new CandidateScore(
            IsbnMatch: isbnMatch ? 1 : 0,
            TitleScore: TitleSimilarity(normalizedTitle, NormalizeForCompare(c.Title)),
            Completeness: Completeness(c),
            ProviderPriority: providerPriority);
    }

    private static int Completeness(ExternalBookMetadataDto c)
    {
        int score = 0;
        if (!string.IsNullOrWhiteSpace(c.Description)) score += 2;
        if (c.Authors.Count > 0) score++;
        if (!string.IsNullOrWhiteSpace(c.Publisher)) score++;
        if (c.PublishedOn is not null) score++;
        if (!string.IsNullOrWhiteSpace(c.Isbn)) score++;
        if (c.PageCount is not null) score++;
        if (!string.IsNullOrWhiteSpace(c.Language)) score++;
        if (c.Genres.Count > 0 || c.Tags.Count > 0) score++;
        if (!string.IsNullOrWhiteSpace(c.Subtitle)) score++;
        return score;
    }

    private static int TitleSimilarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 3;
        }

        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            return 2;
        }

        var tokensA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tokensB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0;
        }

        int shared = tokensA.Count(tokensB.Contains);
        double ratio = (double)shared / Math.Max(tokensA.Count, tokensB.Count);
        return ratio >= 0.5 ? 1 : 0;
    }

    private static string NormalizeForCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                sb.Append(' ');
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Builds one record from the ranked list: the top candidate wins on every field, with its null
    /// scalar fields and empty lists back-filled from lower-ranked results (mirrors Calibre's merge).
    /// </summary>
    private static ExternalBookMetadataDto Merge(IReadOnlyList<ExternalBookMetadataDto> ranked)
    {
        var top = ranked[0];
        var rest = ranked.Skip(1).ToList();

        string? FirstNonBlank(Func<ExternalBookMetadataDto, string?> selector, string? current)
            => !string.IsNullOrWhiteSpace(current) ? current : rest.Select(selector).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        IReadOnlyList<string> FirstNonEmpty(Func<ExternalBookMetadataDto, IReadOnlyList<string>> selector, IReadOnlyList<string> current)
            => current.Count > 0 ? current : rest.Select(selector).FirstOrDefault(v => v.Count > 0) ?? [];

        return top with
        {
            Subtitle = FirstNonBlank(c => c.Subtitle, top.Subtitle),
            Description = FirstNonBlank(c => c.Description, top.Description),
            Language = FirstNonBlank(c => c.Language, top.Language),
            Publisher = FirstNonBlank(c => c.Publisher, top.Publisher),
            Isbn = FirstNonBlank(c => c.Isbn, top.Isbn),
            PublishedOn = top.PublishedOn ?? rest.Select(c => c.PublishedOn).FirstOrDefault(v => v is not null),
            PageCount = top.PageCount ?? rest.Select(c => c.PageCount).FirstOrDefault(v => v is not null),
            SeriesName = FirstNonBlank(c => c.SeriesName, top.SeriesName),
            NumberInSeries = top.NumberInSeries ?? rest.Select(c => c.NumberInSeries).FirstOrDefault(v => v is not null),
            Authors = FirstNonEmpty(c => c.Authors, top.Authors),
            Genres = FirstNonEmpty(c => c.Genres, top.Genres),
            Tags = FirstNonEmpty(c => c.Tags, top.Tags),
            CoverUrl = FirstNonBlank(c => c.CoverUrl, top.CoverUrl),
        };
    }

    private readonly record struct CandidateScore(int IsbnMatch, int TitleScore, int Completeness, int ProviderPriority);
}
