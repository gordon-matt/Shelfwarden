namespace Shelfwarden.Components.Layout;

/// <summary>
/// Shared "Add to reading list" modal. Hosted in <see cref="MainLayout"/> and opened via
/// <see cref="CascadingParameter"/> from <see cref="Shared.AddToReadingListAction"/>.
/// </summary>
public partial class ReadingListPicker : ComponentBase
{
    private string? actionError;
    private bool busy;
    private string? loadError;
    private IReadOnlyList<ReadingListDto> readingLists = [];
    private int selectedReadingListId;

    public int? BookId { get; private set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    public bool IsOpen => BookId is not null || SeriesId is not null;

    public int? SeriesId { get; private set; }

    private string ModalSubtitle => SeriesId is not null
        ? "All books in this series are appended in series order (duplicates are skipped)."
        : "The book is appended to the end of the list (duplicates are skipped).";

    [Inject]
    private IReadingListService ReadingListService { get; set; } = null!;

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        BookId = null;
        SeriesId = null;
        StateHasChanged();
    }

    public void OpenForBook(int bookId)
    {
        BookId = bookId;
        SeriesId = null;
        BeginOpen();
    }

    public void OpenForSeries(int seriesId)
    {
        SeriesId = seriesId;
        BookId = null;
        BeginOpen();
    }

    private void BeginOpen()
    {
        loadError = null;
        actionError = null;
        readingLists = [];
        _ = LoadReadingListsAsync();
        StateHasChanged();
    }

    private async Task ConfirmAsync()
    {
        if (readingLists.Count == 0 || selectedReadingListId <= 0)
        {
            return;
        }

        actionError = null;
        busy = true;
        try
        {
            if (SeriesId is int seriesId)
            {
                var result = await ReadingListService.AddSeriesAsync(selectedReadingListId, seriesId);
                if (result.IsSuccess)
                {
                    Close();
                }
                else
                {
                    actionError = result.Errors.FirstOrDefault() ?? "Could not add series to this list.";
                }
            }
            else if (BookId is int bookId)
            {
                var result = await ReadingListService.AddBookAsync(selectedReadingListId, bookId);
                if (result.IsSuccess)
                {
                    Close();
                }
                else
                {
                    actionError = result.Errors.FirstOrDefault() ?? "Could not add book to this list.";
                }
            }
        }
        finally
        {
            busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadReadingListsAsync()
    {
        busy = true;
        try
        {
            var result = await ReadingListService.ListAsync();
            if (result.IsSuccess)
            {
                readingLists = result.Value;
                selectedReadingListId = readingLists.FirstOrDefault()?.Id ?? 0;
            }
            else
            {
                loadError = result.Errors.FirstOrDefault() ?? "Could not load reading lists.";
            }
        }
        finally
        {
            busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}