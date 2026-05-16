using Microsoft.JSInterop;

namespace Shelfwarden.Components.Pages;

public partial class ExtraContent : ComponentBase
{
    private const int PageSize = 32; // 4 rows × 8 columns on desktop

    private readonly List<AdditionalContentItemDto> loadedItems = [];
    private readonly string observerKey = $"extra-content-{Guid.NewGuid():N}";
    private readonly HashSet<int> selectedIds = [];
    private DotNetObjectReference<ExtraContent>? dotNetRef;

    private IReadOnlyList<AuthorListItemDto>? authors;
    private IReadOnlyList<SeriesListItemDto>? seriesList;

    private int authorFilter;
    private int seriesFilter;

    private int nextPageToLoad = 1;
    private int totalCount;
    private bool hasMorePages;
    private bool isLoadingMore;
    /// <summary>Keeps the loading spinner visible while clearing + refetching so we never flash the empty state.</summary>
    private bool gridReloadPending;
    private bool allSelected;

    private bool scanning;
    private string? scanMessage;
    private string? actionMessage;
    private string? actionError;

    private bool bulkActionBusy;
    private bool bulkAssignOpen;
    private int bulkAssignAuthorId;
    private string? bulkAssignError;

    private ElementReference infiniteScrollSentinel;

    protected override async Task OnInitializedAsync()
    {
        dotNetRef = DotNetObjectReference.Create(this);
        await LoadFiltersAsync();
        await ResetAndLoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!hasMorePages && !isLoadingMore)
        {
            return;
        }

        await JSRuntime.InvokeVoidAsync(
            "shelfwarden.observeInfiniteScroll",
            observerKey,
            infiniteScrollSentinel,
            dotNetRef,
            nameof(LoadMoreAsync));
    }

    private async Task LoadFiltersAsync()
    {
        var authorsTask = AuthorService.ListAsync();
        var seriesTask = SeriesService.ListAsync();
        await Task.WhenAll(authorsTask, seriesTask);

        if (authorsTask.Result.IsSuccess)
        {
            authors = authorsTask.Result.Value;
        }

        if (seriesTask.Result.IsSuccess)
        {
            seriesList = seriesTask.Result.Value;
        }
    }

    private async Task OnFiltersChangedAsync() => await ResetAndLoadAsync();

    private async Task ResetAndLoadAsync()
    {
        gridReloadPending = true;
        try
        {
            loadedItems.Clear();
            nextPageToLoad = 1;
            totalCount = 0;
            hasMorePages = false;
            selectedIds.Clear();
            allSelected = false;
            await LoadMoreAsync();
        }
        finally
        {
            gridReloadPending = false;
        }
    }

    [JSInvokable]
    public async Task LoadMoreAsync()
    {
        if (isLoadingMore || (!hasMorePages && nextPageToLoad > 1))
        {
            return;
        }

        isLoadingMore = true;
        try
        {
            var result = await ContentService.ListPagedAsync(
                nextPageToLoad,
                PageSize,
                authorFilter,
                seriesFilter);

            if (result.IsSuccess)
            {
                var page = result.Value;
                loadedItems.AddRange(page.Items);
                totalCount = page.TotalCount;
                hasMorePages = nextPageToLoad < page.TotalPages;
                nextPageToLoad++;
            }
            else
            {
                hasMorePages = false;
            }
        }
        finally
        {
            isLoadingMore = false;
            SyncAllSelectedState();
            await InvokeAsync(StateHasChanged);
        }
    }

    private void SyncAllSelectedState() =>
        allSelected = loadedItems.Count > 0 && loadedItems.All(i => selectedIds.Contains(i.Id));

    private void ToggleItem(int id, bool include)
    {
        if (include)
        {
            selectedIds.Add(id);
        }
        else
        {
            selectedIds.Remove(id);
        }

        SyncAllSelectedState();
    }

    /// <summary>
    /// After at least one item is selected, clicking the card body toggles membership (same idea
    /// as <see cref="BookCard"/> selection mode on the Books page). The first item is still
    /// chosen via the checkbox or Select all.
    /// </summary>
    private void OnCardHitAreaClick(int itemId)
    {
        if (selectedIds.Count == 0)
        {
            return;
        }

        ToggleItem(itemId, !selectedIds.Contains(itemId));
    }

    private void ToggleSelectAll()
    {
        if (allSelected)
        {
            foreach (var item in loadedItems)
            {
                selectedIds.Add(item.Id);
            }
        }
        else
        {
            selectedIds.Clear();
        }

        SyncAllSelectedState();
    }

    private async Task ScanExtrasAsync()
    {
        scanning = true;
        scanMessage = null;
        try
        {
            var result = await ContentService.ScanExtrasAsync();
            if (result.IsSuccess)
            {
                scanMessage = result.Value > 0
                    ? $"Scan complete — {result.Value} new item(s) discovered."
                    : "Scan complete — no new items found.";
                await ResetAndLoadAsync();
            }
            else
            {
                scanMessage = "Scan failed: " + (result.Errors.FirstOrDefault() ?? "Unknown error.");
            }
        }
        finally
        {
            scanning = false;
        }
    }

    private async Task BulkDeleteAsync()
    {
        if (selectedIds.Count == 0)
        {
            return;
        }

        bulkActionBusy = true;
        actionError = null;
        try
        {
            var result = await ContentService.DeleteAsync(selectedIds.ToList());
            if (result.IsSuccess)
            {
                actionMessage = $"Deleted {selectedIds.Count} item(s).";
                selectedIds.Clear();
                await ResetAndLoadAsync();
            }
            else
            {
                actionError = result.Errors.FirstOrDefault() ?? "Could not delete items.";
            }
        }
        finally
        {
            bulkActionBusy = false;
        }
    }

    private void OpenBulkAssign()
    {
        bulkAssignAuthorId = 0;
        bulkAssignError = null;
        bulkAssignOpen = true;
    }

    private void OpenSingleAssign(AdditionalContentItemDto item)
    {
        selectedIds.Clear();
        selectedIds.Add(item.Id);
        bulkAssignAuthorId = 0;
        bulkAssignError = null;
        bulkAssignOpen = true;
    }

    private async Task BulkAssignToAuthorAsync()
    {
        if (bulkAssignAuthorId == 0 || selectedIds.Count == 0)
        {
            return;
        }

        bulkActionBusy = true;
        bulkAssignError = null;
        try
        {
            var result = await ContentService.AssignToAuthorAsync(new AssignContentToAuthorRequest
            {
                ItemIds = selectedIds.ToList(),
                AuthorId = bulkAssignAuthorId,
            });

            if (result.IsSuccess)
            {
                var author = authors?.FirstOrDefault(a => a.Id == bulkAssignAuthorId);
                actionMessage = $"Assigned {selectedIds.Count} item(s) to {author?.Name ?? "author"}.";
                bulkAssignOpen = false;
                selectedIds.Clear();
                await ResetAndLoadAsync();
            }
            else
            {
                bulkAssignError = result.Errors.FirstOrDefault() ?? "Could not assign items.";
            }
        }
        finally
        {
            bulkActionBusy = false;
        }
    }

    private static bool IsImageExtension(string ext) =>
        ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg";

    private static string GetFileIcon(string ext) => ext switch
    {
        ".pdf" => "bi-filetype-pdf",
        ".txt" => "bi-filetype-txt",
        ".md" => "bi-markdown",
        ".html" or ".htm" => "bi-filetype-html",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" => "bi-image",
        ".zip" => "bi-file-zip",
        _ => "bi-file-earmark",
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    public async ValueTask DisposeAsync()
    {
        dotNetRef?.Dispose();

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
