using Shelfwarden.Components.Layout;

namespace Shelfwarden.Components.Shared;

public partial class AddToReadingListAction : ComponentBase
{
    [CascadingParameter]
    public ReadingListPicker? Picker { get; set; }

    /// <summary>When set, appends this book to the chosen reading list.</summary>
    [Parameter]
    public int? BookId { get; set; }

    /// <summary>When set, appends all books in the series (ordered by <c>NumberInSeries</c>).</summary>
    [Parameter]
    public int? SeriesId { get; set; }

    /// <summary>When true, renders a compact button instead of a dropdown menu item.</summary>
    [Parameter]
    public bool AsButton { get; set; }

    /// <summary>Optional label shown beside the icon when <see cref="AsButton"/> is true.</summary>
    [Parameter]
    public string? ButtonLabel { get; set; }

    private void OpenPicker()
    {
        if (Picker is null)
        {
            return;
        }

        if (SeriesId is int seriesId)
        {
            Picker.OpenForSeries(seriesId);
        }
        else if (BookId is int bookId)
        {
            Picker.OpenForBook(bookId);
        }
    }
}
