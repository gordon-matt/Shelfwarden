using Microsoft.AspNetCore.Components.Web;
using Shelfwarden.Models.Metadata;

namespace Shelfwarden.Components.Shared;

public partial class BookMetadataModal : ComponentBase
{
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Seed values used to pre-fill (and auto-run) the search when the modal opens.</summary>
    [Parameter] public string? InitialTitle { get; set; }

    [Parameter] public string? InitialAuthor { get; set; }

    [Parameter] public string? InitialIsbn { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Raised with the chosen candidate. The parent populates its edit form; nothing is persisted here.</summary>
    [Parameter] public EventCallback<ExternalBookMetadataDto> OnApply { get; set; }

    /// <summary>
    /// Raised with just a cover URL when the user clicks "Use this cover only" — lets them pick a
    /// cover from a different edition than the one whose metadata they apply.
    /// </summary>
    [Parameter] public EventCallback<string> OnUseCover { get; set; }

    private string? searchTitle;
    private string? searchAuthor;
    private string? searchIsbn;

    private bool isSearching;
    private bool hasSearched;
    private string? errorMessage;
    private IReadOnlyList<ExternalBookMetadataDto> results = [];
    private int selectedIndex = -1;

    private bool hasLoadedForCurrentOpenState;

    protected override async Task OnParametersSetAsync()
    {
        if (!IsOpen)
        {
            hasLoadedForCurrentOpenState = false;
            return;
        }

        if (hasLoadedForCurrentOpenState)
        {
            return;
        }

        hasLoadedForCurrentOpenState = true;
        searchTitle = InitialTitle;
        searchAuthor = InitialAuthor;
        searchIsbn = InitialIsbn;
        results = [];
        selectedIndex = -1;
        hasSearched = false;
        errorMessage = null;

        // Auto-search if we have something to go on.
        if (!string.IsNullOrWhiteSpace(searchTitle) || !string.IsNullOrWhiteSpace(searchIsbn))
        {
            await SearchAsync();
        }
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SearchAsync();
        }
    }

    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(searchTitle) && string.IsNullOrWhiteSpace(searchAuthor) && string.IsNullOrWhiteSpace(searchIsbn))
        {
            errorMessage = "Enter a title, author, or ISBN to search.";
            return;
        }

        isSearching = true;
        hasSearched = true;
        errorMessage = null;
        selectedIndex = -1;
        try
        {
            var result = await BookMetadataService.SearchAsync(new BookMetadataSearchRequest
            {
                Title = searchTitle,
                Author = searchAuthor,
                Isbn = searchIsbn,
                Limit = 10,
            });

            if (result.IsSuccess)
            {
                results = result.Value;
                if (results.Count > 0)
                {
                    selectedIndex = 0;
                }
            }
            else
            {
                results = [];
                errorMessage = result.Errors.FirstOrDefault()
                    ?? result.ValidationErrors.Select(v => v.ErrorMessage).FirstOrDefault()
                    ?? "Search failed.";
            }
        }
        catch
        {
            results = [];
            errorMessage = "Something went wrong searching online sources.";
        }
        finally
        {
            isSearching = false;
        }
    }

    private async Task ApplySelectedAsync()
    {
        if (selectedIndex < 0 || selectedIndex >= results.Count)
        {
            return;
        }

        await OnApply.InvokeAsync(results[selectedIndex]);
        await CloseAsync();
    }

    private async Task UseCoverOnlyAsync(ExternalBookMetadataDto match)
    {
        if (string.IsNullOrWhiteSpace(match.CoverUrl))
        {
            return;
        }

        await OnUseCover.InvokeAsync(match.CoverUrl);
        await CloseAsync();
    }

    private async Task CloseAsync() => await OnClose.InvokeAsync();

    private static string Preview(string text)
    {
        const int max = 220;
        string trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : $"{trimmed[..max]}…";
    }
}
