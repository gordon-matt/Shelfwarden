using Shelfwarden.Components.Layout;

namespace Shelfwarden.Components.Shared;

/// <summary>
/// The "Add to collection / reading list / universe" entries shown on book and series tiles and
/// detail pages. Each one opens the shared <see cref="AddToPicker"/> rather than expanding the
/// menu, which would grow unusable once a library has more than a few of any of them.
/// </summary>
public partial class AddToActions : ComponentBase
{
    /// <summary>Renders toolbar buttons instead of dropdown menu items.</summary>
    [Parameter]
    public bool AsButtons { get; set; }

    /// <summary>Adds this book. Ignored when <see cref="SeriesId"/> is set.</summary>
    [Parameter]
    public int? BookId { get; set; }

    [CascadingParameter]
    public AddToPicker? Picker { get; set; }

    /// <summary>Adds every book in this series, in series order.</summary>
    [Parameter]
    public int? SeriesId { get; set; }

    /// <summary>Shown in the dialog so the user can confirm what they picked.</summary>
    [Parameter]
    public string? SubjectName { get; set; }

    /// <summary>Adds text beside the icons. Only meaningful with <see cref="AsButtons"/>.</summary>
    [Parameter]
    public bool ShowLabels { get; set; }

    private void Open(AddToTarget target)
    {
        if (Picker is null)
        {
            return;
        }

        if (SeriesId is int seriesId)
        {
            Picker.OpenForSeries(target, seriesId, SubjectName);
        }
        else if (BookId is int bookId)
        {
            Picker.OpenForBook(target, bookId, SubjectName);
        }
    }
}
