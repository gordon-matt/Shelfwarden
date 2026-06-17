using Microsoft.AspNetCore.Components.Web;
using Shelfwarden.Models.Metadata;

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

    private bool showMetadataModal;

    // Cover changes are applied on Save (covers live as files, not on UpdateBookRequest). Staging a
    // URL queues an online cover; revertCoverToFile queues re-extraction of the embedded cover. Only
    // one can be active at a time.
    private string? pendingCoverUrl;
    private bool revertCoverToFile;

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

        pendingCoverUrl = null;
        revertCoverToFile = false;

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
        form?.NumberInSeries = null;
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

    private void OpenMetadataModal() => showMetadataModal = true;

    private void CloseMetadataModal() => showMetadataModal = false;

    /// <summary>
    /// Populates the edit form from a chosen online candidate. Scalar fields are overwritten only
    /// when the candidate supplies a value; author/genre/tag lists are merged (existing entries are
    /// kept). Nothing is persisted — the user still has to hit Save.
    /// </summary>
    private async Task ApplyExternalMetadataAsync(ExternalBookMetadataDto match)
    {
        if (form is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(match.Title))
        {
            form.Title = match.Title.Trim();
        }

        form.Subtitle = Prefer(match.Subtitle, form.Subtitle);
        form.Description = Prefer(match.Description, form.Description);
        form.Language = Prefer(match.Language, form.Language);
        form.Publisher = Prefer(match.Publisher, form.Publisher);
        form.Isbn = Prefer(match.Isbn, form.Isbn);
        if (match.PublishedOn is { } published)
        {
            form.PublishedOn = published;
        }

        if (!string.IsNullOrWhiteSpace(match.SeriesName))
        {
            await SelectSeriesAsync(match.SeriesName.Trim());
            if (match.NumberInSeries is { } number)
            {
                form.NumberInSeries = number;
            }
        }

        foreach (string author in match.Authors)
        {
            if (!string.IsNullOrWhiteSpace(author) &&
                !selectedAuthors.Any(a => string.Equals(a.Name, author.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                await AddAuthorAsync(author.Trim());
            }
        }

        foreach (string genre in match.Genres)
        {
            if (!string.IsNullOrWhiteSpace(genre) &&
                !selectedGenres.Any(g => string.Equals(g.Name, genre.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                await AddGenreAsync(genre.Trim());
            }
        }

        foreach (string tag in match.Tags)
        {
            await AddTagAsync(tag);
        }

        // Picking an edition also stages its cover (when it has one). The user can undo this in the
        // Cover card if they only wanted the textual metadata.
        if (!string.IsNullOrWhiteSpace(match.CoverUrl))
        {
            StageCoverFromUrl(match.CoverUrl);
        }

        showMetadataModal = false;
    }

    /// <summary>Queues an online cover to apply on save (e.g. the modal's "Use this cover only").</summary>
    private void StageCoverFromUrl(string coverUrl)
    {
        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            return;
        }

        pendingCoverUrl = coverUrl.Trim();
        revertCoverToFile = false;
    }

    /// <summary>Queues a revert to the file's embedded cover on save.</summary>
    private void StageRevertCover()
    {
        revertCoverToFile = true;
        pendingCoverUrl = null;
    }

    private void UndoCoverChange()
    {
        pendingCoverUrl = null;
        revertCoverToFile = false;
    }

    private static string? Prefer(string? incoming, string? current)
        => string.IsNullOrWhiteSpace(incoming) ? current : incoming.Trim();

    private async Task SaveAsync()
    {
        if (form is null || book is null)
        {
            return;
        }

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
            if (!result.IsSuccess)
            {
                errorMessage = result.Errors.FirstOrDefault()
                    ?? string.Join("; ", result.ValidationErrors.Select(v => v.ErrorMessage))
                    ?? "Could not save the book.";
                return;
            }

            // Metadata is saved; now apply any staged cover change. These touch the filesystem, so a
            // failure here is surfaced (the metadata is already persisted) rather than rolled back.
            if (revertCoverToFile)
            {
                var coverResult = await BookCoverService.RevertCoverToEmbeddedAsync(Id);
                if (!coverResult.IsSuccess)
                {
                    errorMessage = coverResult.Errors.FirstOrDefault() ?? "Saved, but the cover could not be reverted.";
                    return;
                }
            }
            else if (!string.IsNullOrWhiteSpace(pendingCoverUrl))
            {
                var coverResult = await BookCoverService.SetCoverFromUrlAsync(Id, pendingCoverUrl);
                if (!coverResult.IsSuccess)
                {
                    errorMessage = coverResult.Errors.FirstOrDefault() ?? "Saved, but the new cover could not be applied.";
                    return;
                }
            }

            NavigationManager.NavigateTo($"books/{Id}");
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