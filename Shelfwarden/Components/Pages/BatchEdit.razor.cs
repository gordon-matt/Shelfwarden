using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Shelfwarden.Extensions;

namespace Shelfwarden.Components.Pages;

public partial class BatchEdit : ComponentBase
{
    [SupplyParameterFromQuery(Name = "ids")]
    public string? IdsFromQuery { get; set; }

    /// <summary>Where to send the user when they hit Cancel/Back. Defaults to the books page.</summary>
    [SupplyParameterFromQuery(Name = "return")]
    public string? ReturnFromQuery { get; set; }

    private string returnUrl = "books";

    private bool loading = true;
    private bool saving;
    private string? errorMessage;
    private string? saveSummary;

    private List<BookDto>? books;
    private readonly List<RowModel> rows = [];
    private readonly HashSet<int> selectedIds = [];

    private bool autoUpdateSortTitle;

    // Bulk apply-to-all state.
    private readonly BulkApplyModel bulk = new();

    private SeriesDto? bulkSeries;
    private string? seriesInput;
    private IReadOnlyList<SeriesDto> seriesSuggestions = [];
    private bool showSeriesSuggestions;

    private readonly List<AuthorDto> bulkAuthors = [];
    private readonly List<GenreDto> bulkGenres = [];
    private readonly List<string> bulkTags = [];

    /// <summary>All existing tag names, loaded once so the tag picker can suggest them.</summary>
    private List<string> tagDirectory = [];

    protected override async Task OnParametersSetAsync()
    {
        if (!string.IsNullOrWhiteSpace(ReturnFromQuery))
        {
            returnUrl = ReturnFromQuery;
        }

        if (tagDirectory.Count == 0)
        {
            var tagsResult = await TagService.ListAsync();
            tagDirectory = tagsResult.IsSuccess
                ? tagsResult.Value
                    .Select(t => t.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];
        }

        var ids = ParseIds(IdsFromQuery);
        if (ids.Count == 0)
        {
            books = [];
            loading = false;
            return;
        }

        loading = true;
        // Fan-out load. ApplicationDbContext is scoped, so we can't run these truly in
        // parallel safely — kick them off sequentially in a small batch.
        var loaded = new List<BookDto>(ids.Count);
        foreach (int id in ids)
        {
            var result = await BookService.GetByIdAsync(id);
            if (result.IsSuccess && result.Value is not null)
            {
                loaded.Add(result.Value);
            }
        }

        books = loaded;
        rows.Clear();
        selectedIds.Clear();
        foreach (var b in loaded)
        {
            rows.Add(new RowModel
            {
                Id = b.Id,
                CoverImagePath = b.CoverImagePath,
                Title = b.Title,
                SortTitle = b.SortTitle,
                Subtitle = b.Subtitle,
                Description = b.Description,
                NumberInSeries = b.NumberInSeries,
            });
            selectedIds.Add(b.Id);
        }

        loading = false;
    }

    private static List<int> ParseIds(string? raw) => string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out int n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .ToList();

    private void ToggleRow(int id, bool include)
    {
        if (include)
        {
            selectedIds.Add(id);
        }
        else
        {
            selectedIds.Remove(id);
        }
    }

    private void ToggleSelectAll(ChangeEventArgs e)
    {
        bool select = (bool)(e.Value ?? false);
        selectedIds.Clear();
        if (select && books is not null)
        {
            foreach (var b in books)
            {
                selectedIds.Add(b.Id);
            }
        }
    }

    private async Task<IReadOnlyList<AuthorDto>> SearchAuthorsAsync(string query)
    {
        var result = await AuthorService.SearchAsync(query, limit: 20);
        return result.IsSuccess ? result.Value : [];
    }

    private async Task AddAuthorAsync(string name)
    {
        var result = await AuthorService.GetOrCreateAsync(name);
        if (result.IsSuccess && !bulkAuthors.Any(a => a.Id == result.Value.Id))
        {
            bulkAuthors.Add(result.Value);
        }
    }

    private async Task<IReadOnlyList<GenreDto>> SearchGenresAsync(string query)
    {
        var result = await GenreService.SearchAsync(query, limit: 20);
        return result.IsSuccess ? result.Value : [];
    }

    private async Task AddGenreAsync(string name)
    {
        var result = await GenreService.GetOrCreateAsync(name);
        if (result.IsSuccess && !bulkGenres.Any(g => g.Id == result.Value.Id))
        {
            bulkGenres.Add(result.Value);
        }
    }

    private async Task OnSeriesInput(ChangeEventArgs e)
    {
        seriesInput = e.Value?.ToString();
        showSeriesSuggestions = true;
        await RefreshSeriesSuggestionsAsync();
    }

    private async Task OnSeriesFocus()
    {
        showSeriesSuggestions = true;
        await RefreshSeriesSuggestionsAsync();
    }

    private async Task OnSeriesKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(seriesInput))
        {
            await SelectSeriesAsync(seriesInput.Trim());
        }
        else if (e.Key == "Escape")
        {
            showSeriesSuggestions = false;
        }
    }

    private async Task RefreshSeriesSuggestionsAsync()
    {
        var result = await SeriesService.SearchAsync(seriesInput, limit: 10);
        seriesSuggestions = result.IsSuccess ? result.Value : [];
    }

    private async Task SelectSeriesAsync(string name)
    {
        var result = await SeriesService.GetOrCreateAsync(name);
        if (result.IsSuccess)
        {
            bulkSeries = result.Value;
            seriesInput = result.Value.Name;
            showSeriesSuggestions = false;
        }
    }

    private Task<IReadOnlyList<string>> SearchTagsAsync(string query)
    {
        string needle = query.Trim().ToLowerInvariant();
        IEnumerable<string> q = tagDirectory;
        if (!string.IsNullOrEmpty(needle))
        {
            q = q.Where(t => t.ToLowerInvariant().Contains(needle));
        }

        return Task.FromResult<IReadOnlyList<string>>(q.Take(50).ToList());
    }

    private Task AddTagAsync(string name)
    {
        string trimmed = name.Trim().TrimStart('#');
        if (!string.IsNullOrEmpty(trimmed) &&
            !bulkTags.Any(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            bulkTags.Add(trimmed);
        }

        return Task.CompletedTask;
    }

    private void OnAfterAutoUpdateSortTitleChanged()
    {
        if (!autoUpdateSortTitle || books is null)
        {
            return;
        }

        foreach (var row in rows)
        {
            ApplyAutoSortTitle(row);
        }
    }

    private void OnTitleChanged(RowModel row)
    {
        if (!autoUpdateSortTitle || books is null)
        {
            return;
        }

        var book = books.FirstOrDefault(b => b.Id == row.Id);
        if (book is null)
        {
            return;
        }

        // When a series is in effect, sort title follows series name + number — updated from # in series, not from title.
        if (!string.IsNullOrWhiteSpace(GetEffectiveSeriesName(book)))
        {
            return;
        }

        row.SortTitle = SortTitleFromBookTitle(row.Title);
    }

    private void OnNumberInSeriesChanged(RowModel row)
    {
        if (!autoUpdateSortTitle || books is null)
        {
            return;
        }

        var book = books.FirstOrDefault(b => b.Id == row.Id);
        if (book is null)
        {
            return;
        }

        string? seriesName = GetEffectiveSeriesName(book);
        if (string.IsNullOrWhiteSpace(seriesName))
        {
            return;
        }

        row.SortTitle = BuildSeriesSortTitle(seriesName, row.NumberInSeries);
    }

    private void ApplyAutoSortTitle(RowModel row)
    {
        if (books is null)
        {
            return;
        }

        var book = books.FirstOrDefault(b => b.Id == row.Id);
        if (book is null)
        {
            return;
        }

        string? seriesName = GetEffectiveSeriesName(book);
        row.SortTitle = !string.IsNullOrWhiteSpace(seriesName)
            ? BuildSeriesSortTitle(seriesName, row.NumberInSeries)
            : SortTitleFromBookTitle(row.Title);
    }

    /// <summary>Series name that will apply when saving: bulk panel override, else the book&apos;s current series.</summary>
    private string? GetEffectiveSeriesName(BookDto book) => bulk.UpdateSeries
            ? string.IsNullOrWhiteSpace(seriesInput)
                ? null
                : bulkSeries is not null &&
                string.Equals(bulkSeries.Name, seriesInput.Trim(), StringComparison.OrdinalIgnoreCase)
                ? bulkSeries.Name
                : seriesInput.Trim()
            : (book.Series?.Name);

    private static string? SortTitleFromBookTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string computed = title.Trim().ToSortTitle();
        return string.IsNullOrWhiteSpace(computed) ? null : computed;
    }

    private static string? BuildSeriesSortTitle(string seriesName, decimal? numberInSeries)
    {
        string name = seriesName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (!numberInSeries.HasValue)
        {
            return name;
        }

        string num = numberInSeries.Value.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{name} {num}";
    }

    private async Task SaveAsync()
    {
        if (books is null || books.Count == 0)
        {
            return;
        }

        saving = true;
        errorMessage = null;
        saveSummary = null;
        try
        {
            // Resolve/create the target series once per save. Previously this only worked when
            // the user explicitly clicked a suggestion/pressed Enter, which made typed values
            // appear to "not work".
            int? resolvedSeriesId = await ResolveSeriesSelectionAsync();

            int updated = 0, failed = 0;
            foreach (var row in rows)
            {
                if (!selectedIds.Contains(row.Id))
                {
                    continue;
                }

                var book = books.FirstOrDefault(b => b.Id == row.Id);
                if (book is null)
                {
                    continue;
                }

                var request = BuildRequest(book, row, resolvedSeriesId);
                var result = await BookService.UpdateAsync(book.Id, request);
                if (result.IsSuccess)
                {
                    updated++;
                }
                else
                {
                    failed++;
                    if (Logger.IsEnabled(LogLevel.Warning))
                    {
                        Logger.LogWarning("Batch update failed for book {BookId}: {Errors}", book.Id, string.Join("; ", result.Errors));
                    }
                }
            }

            saveSummary = failed == 0
                ? $"Updated {updated} book{(updated == 1 ? "" : "s")}."
                : $"Updated {updated} book{(updated == 1 ? "" : "s")}, {failed} failed. Check the logs for details.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Batch save failed");
            errorMessage = "Something went wrong saving. Check the logs for details.";
        }
        finally
        {
            saving = false;
        }
    }

    private async Task<int?> ResolveSeriesSelectionAsync()
    {
        if (!bulk.UpdateSeries)
        {
            return null;
        }

        // Empty input while "Update Series" is enabled means explicit clear.
        if (string.IsNullOrWhiteSpace(seriesInput))
        {
            bulkSeries = null;
            return null;
        }

        // If the selected DTO matches the typed text, keep it.
        if (bulkSeries is not null &&
            string.Equals(bulkSeries.Name, seriesInput.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return bulkSeries.Id;
        }

        // User typed something but didn't click a dropdown item → resolve/create now.
        var result = await SeriesService.GetOrCreateAsync(seriesInput.Trim());
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("Could not resolve the series for batch edit.");
        }

        bulkSeries = result.Value;
        seriesInput = result.Value.Name;
        return result.Value.Id;
    }

    private UpdateBookRequest BuildRequest(BookDto book, RowModel row, int? resolvedSeriesId)
    {
        // Authors / Genres / Tags either replace or append; if the section is disabled we
        // keep the book's current values verbatim. UpdateBookRequest is a full-replace shape
        // so we always have to send a complete list.
        IReadOnlyList<int> authorIds = bulk.UpdateAuthors
            ? bulk.AuthorsMode switch
            {
                ChipApplyMode.Replace => bulkAuthors.Select(a => a.Id).ToList(),
                ChipApplyMode.Remove => [],
                _ => book.Authors.Select(a => a.Id).Concat(bulkAuthors.Select(a => a.Id)).Distinct().ToList(),
            }
            : book.Authors.Select(a => a.Id).ToList();

        IReadOnlyList<int> genreIds = bulk.UpdateGenres
            ? bulk.GenresMode switch
            {
                ChipApplyMode.Replace => bulkGenres.Select(g => g.Id).ToList(),
                ChipApplyMode.Remove => [],
                _ => book.Genres.Select(g => g.Id).Concat(bulkGenres.Select(g => g.Id)).Distinct().ToList(),
            }
            : book.Genres.Select(g => g.Id).ToList();

        IReadOnlyList<string> tags = bulk.UpdateTags
            ? bulk.TagsMode switch
            {
                ChipApplyMode.Replace => bulkTags.ToList(),
                ChipApplyMode.Remove => [],
                _ => book.Tags.Select(t => t.Name).Concat(bulkTags).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            }
            : book.Tags.Select(t => t.Name).ToList();

        // Series: enabled = override (clearing if no series picked); disabled = keep current.
        int? seriesId = bulk.UpdateSeries ? resolvedSeriesId : book.Series?.Id;
        decimal? numberInSeries = seriesId is null ? null : row.NumberInSeries ?? book.NumberInSeries;

        // Publication block now edits publisher/language only.
        string? publisher = bulk.UpdatePublication
            ? bulk.PublicationMode switch
            {
                ChipApplyMode.Replace => bulk.Publisher,
                ChipApplyMode.Remove => null,
                _ => string.IsNullOrWhiteSpace(bulk.Publisher) ? book.Publisher : bulk.Publisher,
            }
            : book.Publisher;
        string? language = bulk.UpdatePublication
            ? bulk.PublicationMode switch
            {
                ChipApplyMode.Replace => bulk.Language,
                ChipApplyMode.Remove => null,
                _ => string.IsNullOrWhiteSpace(bulk.Language) ? book.Language : bulk.Language,
            }
            : book.Language;

        return new UpdateBookRequest
        {
            Title = string.IsNullOrWhiteSpace(row.Title) ? book.Title : row.Title.Trim(),
            SortTitle = string.IsNullOrWhiteSpace(row.SortTitle) ? null : row.SortTitle!.Trim(),
            Subtitle = string.IsNullOrWhiteSpace(row.Subtitle) ? null : row.Subtitle!.Trim(),
            Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description,
            Language = string.IsNullOrWhiteSpace(language) ? null : language,
            Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher,
            Isbn = book.Isbn,
            PublishedOn = book.PublishedOn,
            SeriesId = seriesId,
            NumberInSeries = numberInSeries,
            AuthorIds = authorIds,
            GenreIds = genreIds,
            Tags = tags,
        };
    }

    private sealed class RowModel
    {
        public int Id { get; set; }
        public string? CoverImagePath { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? SortTitle { get; set; }
        public string? Subtitle { get; set; }
        public string? Description { get; set; }
        public decimal? NumberInSeries { get; set; }
    }

    private sealed class BulkApplyModel
    {
        public bool UpdateSeries { get; set; }
        public bool UpdateAuthors { get; set; }
        public ChipApplyMode AuthorsMode { get; set; } = ChipApplyMode.Append;
        public bool UpdateGenres { get; set; }
        public ChipApplyMode GenresMode { get; set; } = ChipApplyMode.Append;
        public bool UpdateTags { get; set; }
        public ChipApplyMode TagsMode { get; set; } = ChipApplyMode.Append;
        public bool UpdatePublication { get; set; }
        public ChipApplyMode PublicationMode { get; set; } = ChipApplyMode.Append;
        public string? Publisher { get; set; }
        public string? Language { get; set; }
    }
}