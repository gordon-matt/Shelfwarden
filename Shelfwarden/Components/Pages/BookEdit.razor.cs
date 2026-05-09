using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Pages;

public partial class BookEdit : ComponentBase
{
    [Parameter]
    public int Id { get; set; }

    private BookDto? book;
    private FormModel? form;
    private bool loading = true;
    private bool saving;
    private string? errorMessage;

    // Authors / Genres are managed as the actual selected DTOs so we already have the IDs
    // for save without re-resolving them. New entries get an Id of 0 → resolved on save.
    private readonly List<AuthorDto> selectedAuthors = [];

    private readonly List<GenreDto> selectedGenres = [];
    private readonly List<string> selectedTags = [];
    private List<string> tagDirectory = [];

    // Series picker — single-select with create-on-the-fly. We track the *name* the user
    // typed/picked here and resolve it to an Id at save time so the user can create new
    // series without an extra round-trip.
    private SeriesDto? selectedSeries;

    private string? seriesInput;
    private IReadOnlyList<SeriesDto> seriesSuggestions = [];
    private bool showSeriesSuggestions;

    protected override async Task OnParametersSetAsync()
    {
        loading = true;
        var bookTask = BookService.GetByIdAsync(Id);
        var tagsTask = TagService.ListAsync();
        await Task.WhenAll(bookTask, tagsTask);

        var tagsLookup = await tagsTask;
        tagDirectory = tagsLookup.IsSuccess
            ? tagsLookup.Value.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
            : [];

        var result = await bookTask;
        if (!result.IsSuccess || result.Value is null)
        {
            book = null;
            loading = false;
            return;
        }

        book = result.Value;
        form = new FormModel
        {
            Title = book.Title,
            SortTitle = book.SortTitle,
            Subtitle = book.Subtitle,
            Description = book.Description,
            Language = book.Language,
            Publisher = book.Publisher,
            Isbn = book.Isbn,
            PublishedOn = book.PublishedOn,
            NumberInSeries = book.NumberInSeries,
        };

        selectedAuthors.Clear();
        selectedAuthors.AddRange(book.Authors);
        selectedGenres.Clear();
        selectedGenres.AddRange(book.Genres);
        selectedTags.Clear();
        selectedTags.AddRange(book.Tags.Select(t => t.Name));
        selectedSeries = book.Series;
        seriesInput = book.Series?.Name;

        loading = false;
    }

    private async Task<IReadOnlyList<AuthorDto>> SearchAuthorsAsync(string query)
    {
        var result = await AuthorService.SearchAsync(query, limit: 20);
        return result.IsSuccess ? result.Value : [];
    }

    private async Task AddAuthorAsync(string name)
    {
        var result = await AuthorService.GetOrCreateAsync(name);
        if (result.IsSuccess)
        {
            selectedAuthors.Add(result.Value);
        }
        else if (Logger.IsEnabled(LogLevel.Warning))
        {
            Logger.LogWarning("Failed to add author '{Name}': {Errors}", name, string.Join("; ", result.Errors));
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
        if (result.IsSuccess)
        {
            selectedGenres.Add(result.Value);
        }
    }

    private async Task OnSeriesInput(ChangeEventArgs e)
    {
        seriesInput = e.Value?.ToString();
        if (string.IsNullOrWhiteSpace(seriesInput))
        {
            ClearSeries();
            showSeriesSuggestions = false;
            return;
        }

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
            selectedSeries = result.Value;
            seriesInput = result.Value.Name;
            showSeriesSuggestions = false;
        }
    }

    private void ClearSeries()
    {
        selectedSeries = null;
        seriesInput = null;
        if (form is not null)
        {
            form.NumberInSeries = null;
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
        var trimmed = name.Trim().TrimStart('#');
        if (string.IsNullOrEmpty(trimmed))
        {
            return Task.CompletedTask;
        }

        if (!selectedTags.Any(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            selectedTags.Add(trimmed);
        }

        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (form is null || book is null) return;
        saving = true;
        errorMessage = null;
        try
        {
            int? resolvedSeriesId = null;
            decimal? resolvedNumberInSeries = null;
            if (!string.IsNullOrWhiteSpace(seriesInput))
            {
                var seriesResult = await SeriesService.GetOrCreateAsync(seriesInput.Trim());
                if (!seriesResult.IsSuccess)
                {
                    errorMessage = seriesResult.Errors.FirstOrDefault()
                        ?? "Could not resolve the series.";
                    return;
                }

                resolvedSeriesId = seriesResult.Value.Id;
                resolvedNumberInSeries = form.NumberInSeries;
            }

            var request = new UpdateBookRequest
            {
                Title = form.Title,
                SortTitle = string.IsNullOrWhiteSpace(form.SortTitle) ? null : form.SortTitle,
                Subtitle = string.IsNullOrWhiteSpace(form.Subtitle) ? null : form.Subtitle,
                Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description,
                Language = string.IsNullOrWhiteSpace(form.Language) ? null : form.Language,
                Publisher = string.IsNullOrWhiteSpace(form.Publisher) ? null : form.Publisher,
                Isbn = string.IsNullOrWhiteSpace(form.Isbn) ? null : form.Isbn,
                PublishedOn = form.PublishedOn,
                SeriesId = resolvedSeriesId,
                NumberInSeries = resolvedSeriesId is null ? null : resolvedNumberInSeries,
                AuthorIds = selectedAuthors.Select(a => a.Id).ToList(),
                GenreIds = selectedGenres.Select(g => g.Id).ToList(),
                Tags = selectedTags,
            };

            var result = await BookService.UpdateAsync(Id, request);
            if (result.IsSuccess)
            {
                NavigationManager.NavigateTo($"books/{Id}");
            }
            else
            {
                errorMessage = result.Errors.FirstOrDefault()
                    ?? string.Join("; ", result.ValidationErrors.Select(v => v.ErrorMessage))
                    ?? "Could not save the book.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save metadata for book {BookId}", Id);
            errorMessage = "Something went wrong saving the book. Check the logs for details.";
        }
        finally
        {
            saving = false;
        }
    }

    private sealed class FormModel
    {
        [Required, StringLength(512)]
        public string Title { get; set; } = string.Empty;

        [StringLength(512)]
        public string? SortTitle { get; set; }

        [StringLength(512)]
        public string? Subtitle { get; set; }

        public string? Description { get; set; }

        [StringLength(16)]
        public string? Language { get; set; }

        [StringLength(256)]
        public string? Publisher { get; set; }

        [StringLength(32)]
        public string? Isbn { get; set; }

        public DateTime? PublishedOn { get; set; }

        public decimal? NumberInSeries { get; set; }
    }
}