namespace Shelfwarden.Components.Pages;

public partial class ShelfDetail : ComponentBase
{
    [Parameter]
    public int Id { get; set; }

    private ShelfDto? shelf;
    private readonly List<BookListItemDto> books = [];
    private readonly HashSet<int> selectedBookIds = [];
    private readonly string observerKey = $"shelf-books-{Guid.NewGuid():N}";
    private ViewMode viewMode = ViewMode.Grid;
    private int totalBookCount;
    private bool selectAllMatchingBusy;
    private bool scanIndicatorVisible;
    private DateTime? optimisticBusyUntilUtc;
    private CancellationTokenSource? pollCts;
    private ElementReference infiniteScrollSentinel;
    private DotNetObjectReference<ShelfDetail>? dotNetRef;
    private int pageNumber = 1;
    private const int PageSize = 48;
    private bool hasMoreBooks;
    private bool isLoadingMoreBooks;
    private string? startsWithFilter;

    private enum ViewMode
    { Grid, List }

    private int BulkEditMaxBookCount => Math.Max(1, Configuration.GetValue<int?>("BulkEditMaxBookCount") ?? 100);
    private bool CanSelectAllMatching => totalBookCount > 0 && totalBookCount <= BulkEditMaxBookCount;

    protected override async Task OnParametersSetAsync()
    {
        var lib = await ShelfService.GetByIdAsync(Id);
        shelf = lib.IsSuccess ? lib.Value : null;

        selectedBookIds.Clear();
        await ResetBooksAsync();
    }

    protected override void OnInitialized()
    {
        pollCts = new CancellationTokenSource();
        dotNetRef = DotNetObjectReference.Create(this);
        _ = Task.Run(() => PollLoopAsync(pollCts.Token));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Infinite-scroll sentinel is only in the DOM once books are rendered; during the
        // initial "Loading books..." state (books empty, still loading) there is no element.
        if (books.Count == 0)
        {
            return;
        }

        if (!hasMoreBooks && !isLoadingMoreBooks)
        {
            return;
        }

        await JSRuntime.InvokeVoidAsync(
            "shelfwarden.observeInfiniteScroll",
            observerKey,
            infiniteScrollSentinel,
            dotNetRef,
            nameof(LoadMoreBooksAsync));
    }

    private async Task ScanAsync()
    {
        scanIndicatorVisible = true;
        optimisticBusyUntilUtc = DateTime.UtcNow.AddSeconds(20);
        StateHasChanged();
        var result = await ShelfService.ScheduleScanAsync(Id);
        if (!result.IsSuccess)
        {
            scanIndicatorVisible = false;
            optimisticBusyUntilUtc = null;
        }
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5), token);
                if (token.IsCancellationRequested)
                {
                    break;
                }

                await RefreshScanIndicatorAsync(token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshScanIndicatorAsync(CancellationToken cancellationToken)
    {
        var statusResult = await ScanStatusService.GetForShelfAsync(Id, cancellationToken);
        if (!statusResult.IsSuccess)
        {
            return;
        }

        var status = statusResult.Value;
        bool optimisticBusy = optimisticBusyUntilUtc is { } until && until > DateTime.UtcNow;
        bool busy = IsBusy(status) || optimisticBusy;
        bool changed = scanIndicatorVisible != busy;
        scanIndicatorVisible = busy;

        // Scan just finished: refresh shelf + books so last-scan timestamp/counts are current.
        if (!IsBusy(status) && !optimisticBusy && changed && shelf is not null)
        {
            optimisticBusyUntilUtc = null;
            await OnParametersSetAsync();
        }

        // Once the backend reports real busy state, we no longer need optimistic hold.
        if (IsBusy(status))
        {
            optimisticBusyUntilUtc = null;
        }

        if (changed)
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private static bool IsBusy(ScanStatusDto? status)
        => status is not null && (status.State == ScanState.Running || status.State == ScanState.Queued);

    private async Task DeleteAsync()
    {
        var result = await ShelfService.DeleteAsync(Id);
        if (result.IsSuccess)
        {
            SidebarNavRefresh.NotifyNavigationDataChanged();
            NavigationManager.NavigateTo("shelves");
        }
    }

    private void ToggleSelection(int id, bool include)
    {
        if (include)
        {
            selectedBookIds.Add(id);
        }
        else
        {
            selectedBookIds.Remove(id);
        }
    }

    private void SelectAllVisible()
    {
        foreach (var b in books)
        {
            selectedBookIds.Add(b.Id);
        }
    }

    private async Task SelectAllMatchingAsync()
    {
        if (selectAllMatchingBusy || shelf is null || !CanSelectAllMatching)
        {
            return;
        }

        selectAllMatchingBusy = true;
        try
        {
            const int batch = 200;
            int page = 1;
            while (true)
            {
                var bookResult = await BookService.SearchAsync(new BookSearchRequest
                {
                    ShelfId = Id,
                    Page = page,
                    PageSize = batch,
                    SortBy = BookSortBy.Title,
                    StartsWith = startsWithFilter,
                });

                if (!bookResult.IsSuccess)
                {
                    break;
                }

                var p = bookResult.Value;
                foreach (var b in p.Items)
                {
                    selectedBookIds.Add(b.Id);
                }

                if (page >= p.TotalPages)
                {
                    break;
                }

                page++;
            }
        }
        finally
        {
            selectAllMatchingBusy = false;
        }
    }

    private void ClearSelection() => selectedBookIds.Clear();

    private void GoToBatchEdit()
    {
        if (selectedBookIds.Count == 0)
        {
            return;
        }

        string ids = string.Join(',', selectedBookIds);
        NavigationManager.NavigateTo($"books/batch-edit?ids={ids}&return=shelves/{Id}");
    }

    private async Task ResetBooksAsync()
    {
        pageNumber = 1;
        books.Clear();
        hasMoreBooks = false;
        await LoadMoreBooksAsync();
    }

    [JSInvokable]
    public async Task LoadMoreBooksAsync()
    {
        if (isLoadingMoreBooks || (!hasMoreBooks && pageNumber > 1) || shelf is null)
        {
            return;
        }

        isLoadingMoreBooks = true;

        var bookResult = await BookService.SearchAsync(new BookSearchRequest
        {
            ShelfId = Id,
            Page = pageNumber,
            PageSize = PageSize,
            SortBy = BookSortBy.Title,
            StartsWith = startsWithFilter,
        });

        if (bookResult.IsSuccess)
        {
            var page = bookResult.Value;
            totalBookCount = page.TotalCount;
            books.AddRange(page.Items);
            hasMoreBooks = pageNumber < page.TotalPages;
            pageNumber++;
        }
        else
        {
            hasMoreBooks = false;
        }

        isLoadingMoreBooks = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnLetterRailChangedAsync()
    {
        selectedBookIds.Clear();
        await ResetBooksAsync();
    }

    private async Task ClearLetterFilterAsync()
    {
        startsWithFilter = null;
        selectedBookIds.Clear();
        await ResetBooksAsync();
    }

    public void Dispose()
    {
        pollCts?.Cancel();
        pollCts?.Dispose();
        dotNetRef?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("shelfwarden.disconnectInfiniteScroll", observerKey);
        }
        catch
        {
            // Ignore disposal-time JS failures during teardown.
        }
    }
}