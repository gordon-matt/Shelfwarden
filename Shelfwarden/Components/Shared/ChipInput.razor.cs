using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Shared;

public partial class ChipInput<TItem> : ComponentBase
    where TItem : class
{
    private CancellationTokenSource? blurCts;
    private string? input;
    private CancellationTokenSource? searchCts;
    private bool showSuggestions;
    private IReadOnlyList<TItem> suggestions = [];

    /// <summary>When false, the "create from typed text" option is hidden (e.g. book pickers).</summary>
    [Parameter] public bool AllowCreate { get; set; } = true;

    /// <summary>Optional extra CSS class on each chip (e.g. <c>chip-tag</c> on the book edit form).</summary>
    [Parameter] public string? ChipCssSuffix { get; set; }

    [Parameter, EditorRequired] public string Label { get; set; } = "Item";

    /// <summary>Map a TItem to its display name. Required because the component is generic.</summary>
    [Parameter, EditorRequired] public Func<TItem, string> NameOf { get; set; } = _ => string.Empty;

    /// <summary>
    /// Called when the user picks an existing suggestion or types a new name. The parent is
    /// responsible for resolving the name to a real entity (e.g. via GetOrCreateAsync) and
    /// adding it into <see cref="Selected"/>.
    /// </summary>
    [Parameter, EditorRequired] public Func<string, Task> OnAddAsync { get; set; } = _ => Task.CompletedTask;

    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>Returns suggestions for the current input. Called whenever the input changes.</summary>
    [Parameter, EditorRequired]
    public Func<string, Task<IReadOnlyList<TItem>>> SearchAsync { get; set; } =
        _ => Task.FromResult<IReadOnlyList<TItem>>([]);

    [Parameter, EditorRequired] public IList<TItem> Selected { get; set; } = [];

    private async Task CommitAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // Skip duplicates (case-insensitive).
        if (Selected.Any(s => string.Equals(NameOf(s), name, StringComparison.OrdinalIgnoreCase)))
        {
            input = string.Empty;
            return;
        }

        await OnAddAsync(name);
        input = string.Empty;
        suggestions = [];
        showSuggestions = false;
        if (OnChanged.HasDelegate)
        {
            await OnChanged.InvokeAsync();
        }
    }

    private async Task OnBlurAsync()
    {
        blurCts?.Cancel();
        blurCts = new CancellationTokenSource();
        var token = blurCts.Token;
        try
        {
            // Brief delay so mousedown on a suggestion still registers before the menu hides.
            await Task.Delay(150, token);
            showSuggestions = false;
            await InvokeAsync(StateHasChanged);
        }
        catch (TaskCanceledException) { }
    }

    private async Task OnFocusAsync()
    {
        blurCts?.Cancel();
        showSuggestions = true;
        await RefreshSuggestionsAsync();
    }

    private async Task OnInputChangedAsync()
    {
        showSuggestions = true;
        await RefreshSuggestionsAsync();
    }

    private async Task OnKeyUpAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(input))
        {
            await CommitAsync(input.Trim());
            return;
        }

        if (e.Key == "Escape")
        {
            showSuggestions = false;
        }
    }

    private async Task RefreshSuggestionsAsync()
    {
        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;
        try
        {
            // Allow a 150ms breath so we're not hammering the backend on every keystroke.
            await Task.Delay(150, token);
            var results = await SearchAsync(input ?? string.Empty);
            if (token.IsCancellationRequested)
            {
                return;
            }

            // Drop anything already selected — exact case-insensitive match on name.
            var selectedNames = Selected.Select(NameOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
            suggestions = results.Where(r => !selectedNames.Contains(NameOf(r))).Take(10).ToList();
            await InvokeAsync(StateHasChanged);
        }
        catch (TaskCanceledException) { }
    }

    private async Task RemoveAsync(TItem item)
    {
        Selected.Remove(item);
        if (OnChanged.HasDelegate)
        {
            await OnChanged.InvokeAsync();
        }
    }
}