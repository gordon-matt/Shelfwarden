namespace Shelfwarden.Components.Pages;

public partial class SeriesDetail : ComponentBase
{
    // Assign-content modal state
    private bool assignModalOpen;

    private int assignTargetBookId;
    private string? assignTargetBookTitle;
    private IReadOnlyList<BookListItemDto>? books;
    private IReadOnlyList<AdditionalContentItemDto>? extraContent;
    private bool loading = true;
    private SeriesDto? series;
    [Parameter] public int Id { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        loading = true;
        series = null;
        books = null;
        extraContent = null;
        assignModalOpen = false;

        var seriesResult = await SeriesService.GetByIdAsync(Id);
        if (seriesResult.IsSuccess)
        {
            series = seriesResult.Value;

            // Load books and extra content in parallel.
            var booksTask = BookService.SearchAsync(new BookSearchRequest
            {
                SeriesId = Id,
                PageSize = 500,
                SortBy = BookSortBy.NumberInSeries,
            });
            var contentTask = ContentService.GetForSeriesAsync(Id);

            await Task.WhenAll(booksTask, contentTask);

            if (booksTask.Result.IsSuccess)
            {
                books = booksTask.Result.Value.Items;
            }
            if (contentTask.Result.IsSuccess)
            {
                extraContent = contentTask.Result.Value;
            }
        }

        loading = false;
    }

    private void CloseAssignModal() => assignModalOpen = false;

    private async Task OnContentAssociatedAsync()
    {
        assignModalOpen = false;
        await RefreshExtraContentAsync();
    }

    private void OnItemRenamed(AdditionalContentItemDto renamed)
    {
        if (extraContent is null) return;
        extraContent = extraContent
            .Select(i => i.Id == renamed.Id ? renamed : i)
            .ToList();
    }

    private void OpenAssignContentBook(int bookId, string bookTitle)
    {
        assignTargetBookId = bookId;
        assignTargetBookTitle = bookTitle;
        assignModalOpen = true;
    }

    private async Task RefreshExtraContentAsync()
    {
        var result = await ContentService.GetForSeriesAsync(Id);
        if (result.IsSuccess)
        {
            extraContent = result.Value;
            StateHasChanged();
        }
    }
}