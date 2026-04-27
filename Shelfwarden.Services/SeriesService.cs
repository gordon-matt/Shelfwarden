namespace Shelfwarden.Services;

public class SeriesService(
    IRepository<Series> seriesRepository,
    IRepository<Book> bookRepository) : ISeriesService
{
    public async Task<Result<IReadOnlyList<SeriesDto>>> SearchAsync(string? query, int limit = 50, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Series>
        {
            PageNumber = 1,
            PageSize = Math.Clamp(limit, 1, 200),
            OrderBy = q => q.OrderBy(s => s.NormalizedName),
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = s => EF.Functions.Like(s.NormalizedName, $"%{needle}%");
        }

        var seriesList = (await seriesRepository.FindAsync(options)).ToList();
        var ids = seriesList.Select(s => s.Id).ToList();

        var counts = (await bookRepository.FindAsync(
                new SearchOptions<Book> { Query = b => b.SeriesId != null && ids.Contains(b.SeriesId.Value) },
                b => new { b.SeriesId }))
            .Where(x => x.SeriesId.HasValue)
            .GroupBy(x => x.SeriesId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        IReadOnlyList<SeriesDto> result = seriesList
            .Select(s => new SeriesDto(s.Id, s.Name, s.Description, counts.GetValueOrDefault(s.Id, 0)))
            .ToList();
        return Result.Success(result);
    }

    public async Task<Result<SeriesDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var series = await seriesRepository.FindOneAsync(new SearchOptions<Series>
        {
            Query = s => s.Id == id,
        });
        if (series is null)
        {
            return Result.NotFound();
        }

        int count = (await bookRepository.FindAsync(
                new SearchOptions<Book> { Query = b => b.SeriesId == id },
                b => b.Id))
            .Count();

        return Result.Success(new SeriesDto(series.Id, series.Name, series.Description, count));
    }

    public async Task<Result<SeriesDto>> GetOrCreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Invalid(new ValidationError(nameof(name), "Name is required."));
        }

        string trimmed = name.Trim();
        string normalised = trimmed.ToLowerInvariant();

        var existing = await seriesRepository.FindOneAsync(new SearchOptions<Series>
        {
            Query = s => s.NormalizedName == normalised,
        });
        if (existing is not null)
        {
            return Result.Success(new SeriesDto(existing.Id, existing.Name, existing.Description, BookCount: 0));
        }

        var created = await seriesRepository.InsertAsync(new Series
        {
            Name = trimmed,
            NormalizedName = normalised,
        });
        return Result.Success(new SeriesDto(created.Id, created.Name, created.Description, BookCount: 0));
    }

    public async Task<Result<IReadOnlyList<SeriesListItemDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Series>
        {
            OrderBy = q => q.OrderBy(s => s.NormalizedName),
            CancellationToken = cancellationToken,
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = s => EF.Functions.Like(s.NormalizedName, $"%{needle}%");
        }

        var seriesList = (await seriesRepository.FindAsync(options)).ToList();
        var ids = seriesList.Select(s => s.Id).ToList();

        // Pull just the (id, seriesId, number, cover) tuples we need for the collage.
        // Anonymous projection keeps us off the wire from yanking down full Book rows.
        var bookRows = (await bookRepository.FindAsync(
                new SearchOptions<Book>
                {
                    Query = b => b.SeriesId != null && ids.Contains(b.SeriesId.Value),
                    OrderBy = q => q.OrderBy(b => b.NumberInSeries).ThenBy(b => b.SortTitle ?? b.Title),
                    CancellationToken = cancellationToken,
                },
                b => new { b.Id, b.SeriesId, b.CoverImagePath }))
            .ToList();

        var bySeries = bookRows
            .Where(b => b.SeriesId.HasValue)
            .GroupBy(b => b.SeriesId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        IReadOnlyList<SeriesListItemDto> result = seriesList
            .Select(s =>
            {
                bySeries.TryGetValue(s.Id, out var rows);
                rows ??= [];
                var covers = rows
                    .Take(4)
                    .Select(r => new SeriesCoverDto(r.Id, r.CoverImagePath))
                    .ToList();
                return new SeriesListItemDto(s.Id, s.Name, s.Description, rows.Count, covers);
            })
            .ToList();

        return Result.Success(result);
    }
}
