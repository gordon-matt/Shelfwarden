namespace Shelfwarden.Components.Shared;

public partial class CardHeaderImageSelector : ComponentBase
{
    private readonly string _groupName = $"hdr-{Guid.NewGuid():N}";

    [Parameter, EditorRequired] public CardHeaderBannerMode Mode { get; set; }

    [Parameter] public EventCallback<CardHeaderBannerMode> ModeChanged { get; set; }

    [Parameter, EditorRequired] public List<BookListItemDto> SelectedBooks { get; set; } = [];

    [Parameter] public EventCallback OnSelectedBooksChanged { get; set; }

    /// <summary>Search within the current shelf / collection / reading list only.</summary>
    [Parameter, EditorRequired] public Func<string, Task<IReadOnlyList<BookListItemDto>>> SearchBooksInScopeAsync { get; set; } = null!;

    [Parameter, EditorRequired] public Func<IBrowserFile, Task> OnUploadAsync { get; set; } = null!;

    private string bookQuery = string.Empty;
    private bool showSuggestions;
    private IReadOnlyList<BookListItemDto> bookSuggestions = [];
    private CancellationTokenSource? searchCts;

    private async Task SetMode(CardHeaderBannerMode mode)
    {
        Mode = mode;
        if (ModeChanged.HasDelegate)
        {
            await ModeChanged.InvokeAsync(mode);
        }
    }

    private async Task OnFileChosenAsync(InputFileChangeEventArgs e)
    {
        IBrowserFile? file = e.File;
        if (file is null)
        {
            return;
        }

        try
        {
            await OnUploadAsync(file);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Card banner upload failed");
        }
    }

    private async Task AddBookAsync(BookListItemDto book)
    {
        if (SelectedBooks.Count >= CardBannerLimits.MaxStripCovers)
        {
            return;
        }

        if (SelectedBooks.Any(b => b.Id == book.Id))
        {
            bookQuery = string.Empty;
            bookSuggestions = [];
            return;
        }

        SelectedBooks.Add(book);
        bookQuery = string.Empty;
        bookSuggestions = [];
        showSuggestions = false;
        if (OnSelectedBooksChanged.HasDelegate)
        {
            await OnSelectedBooksChanged.InvokeAsync();
        }
    }

    private async Task RemoveBookAsync(BookListItemDto book)
    {
        SelectedBooks.Remove(book);
        if (OnSelectedBooksChanged.HasDelegate)
        {
            await OnSelectedBooksChanged.InvokeAsync();
        }
    }

    private async Task OnBookQueryInput(ChangeEventArgs e)
    {
        bookQuery = e.Value?.ToString() ?? string.Empty;
        showSuggestions = true;
        await RefreshSuggestionsAsync();
    }

    private async Task RefreshSuggestionsAsync()
    {
        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;
        try
        {
            await Task.Delay(120, token);
            var results = await SearchBooksInScopeAsync(bookQuery ?? string.Empty);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var taken = new HashSet<int>(SelectedBooks.Select(b => b.Id));
            bookSuggestions = results.Where(b => !taken.Contains(b.Id)).Take(12).ToList();
            await InvokeAsync(StateHasChanged);
        }
        catch (TaskCanceledException) { }
    }
}