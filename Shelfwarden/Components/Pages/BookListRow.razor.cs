namespace Shelfwarden.Components.Pages;

public partial class BookListRow : ComponentBase
{
    [Parameter, EditorRequired]
    public BookListItemDto? Book { get; set; }

    /// <summary>Hard truncate length for the description preview. ~280 chars fits ~3 lines on desktop without crowding the row.</summary>
    [Parameter]
    public int DescriptionLength { get; set; } = 280;

    /// <summary>Whether this row is currently part of the parent's selection set.</summary>
    [Parameter]
    public bool IsSelected { get; set; }

    /// <summary>Raised with the desired new selection state when the row is clicked in selection mode.</summary>
    [Parameter]
    public EventCallback<bool> OnSelectionToggled { get; set; }

    /// <summary>
    /// When true, clicking the row toggles selection via <see cref="OnSelectionToggled"/> instead
    /// of navigating to the book detail page. Mirrors the same parameter on
    /// <see cref="BookCard"/> so list and grid views behave identically when the user is
    /// multi-selecting.
    /// </summary>
    [Parameter]
    public bool SelectionMode { get; set; }

    private static string Truncate(string raw, int max)
    {
        // Strip HTML tags so a description containing inline markup doesn't break the layout
        // or leak partial elements when we cut it short. The detail page still renders full HTML.
        string plain = System.Text.RegularExpressions.Regex
            .Replace(raw, "<.*?>", string.Empty)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (plain.Length <= max)
        {
            return plain;
        }
        // Try to break at a word boundary so we don't slice mid-word.
        int cut = plain.LastIndexOf(' ', max);
        if (cut < max / 2)
        {
            cut = max;
        }

        return plain[..cut].TrimEnd(',', '.', ';', ':') + "…";
    }

    private Task HandleRowClickAsync() => !SelectionMode || Book is null || !OnSelectionToggled.HasDelegate
                ? Task.CompletedTask
            : OnSelectionToggled.InvokeAsync(!IsSelected);
}