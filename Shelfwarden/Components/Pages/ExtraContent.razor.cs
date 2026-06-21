namespace Shelfwarden.Components.Pages;

public partial class ExtraContent : ComponentBase
{
    private const int PageSize = 32;
    private readonly List<AdditionalContentItemDto> loadedItems = [];

    // 4 rows × 8 columns on desktop
    private readonly string observerKey = $"extra-content-{Guid.NewGuid():N}";

    private readonly HashSet<int> selectedIds = [];
    private readonly List<AdditionalContentTagDto> selectedTagFilters = [];
    private string? actionError;
    private string? actionMessage;
    private bool addContentBusy;
    private bool allSelected;
    private List<AdditionalContentTagDto> allTags = [];
    private int authorFilter = -1;
    private IReadOnlyList<AuthorListItemDto>? authors;
    private bool bulkActionBusy;
    private int bulkAssignAuthorId;
    private string? bulkAssignError;
    private bool bulkAssignOpen;
    private DotNetObjectReference<ExtraContent>? dotNetRef;

    /// <summary>Keeps the loading spinner visible while clearing + refetching so we never flash the empty state.</summary>
    private bool gridReloadPending;

    private bool hasMorePages;
    private ElementReference infiniteScrollSentinel;
    private bool isLoadingMore;
    private int nextPageToLoad = 1;
    private string? scanMessage;
    private bool scanning;
    private int seriesFilter = 0;
    private IReadOnlyList<SeriesListItemDto>? seriesList;
    private bool showAddContentPicker;
    private TagFilterMode tagFilterMode = TagFilterMode.Any;
    private List<string> tagsModalInitialNames = [];
    private List<int> tagsModalItemIds = [];
    private bool tagsModalOpen;
    private string tagsModalTitle = "Edit tags";
    private int totalCount;

    private enum TagFilterMode
    {
        Any,
        None,
        Selected,
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
                seriesFilter,
                GetTagFilterId(),
                GetSelectedTagFilterIds());

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

    protected override async Task OnInitializedAsync()
    {
        dotNetRef = DotNetObjectReference.Create(this);
        await LoadFiltersAsync();
        await ResetAndLoadAsync();
    }

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

    private static bool IsImageExtension(string ext) =>
        ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg";

    private Task AddTagFilterAsync(string tagName)
    {
        string trimmed = tagName.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        var existing = allTags.FirstOrDefault(t =>
            string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !selectedTagFilters.Any(t => t.Id == existing.Id))
        {
            selectedTagFilters.Add(existing);
        }

        return Task.CompletedTask;
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

    private void CancelAddContentPicker() => showAddContentPicker = false;

    private void CloseTagsModal() => tagsModalOpen = false;

    private IReadOnlyList<int> GetSelectedTagFilterIds()
        => tagFilterMode == TagFilterMode.Selected
            ? selectedTagFilters.Select(t => t.Id).Distinct().ToList()
            : [];

    private int? GetTagFilterId() => tagFilterMode switch
    {
        TagFilterMode.None => -1,
        _ => null,
    };

    private async Task LoadFiltersAsync()
    {
        var authorsTask = AuthorService.ListAsync();
        var seriesTask = SeriesService.ListAsync();
        var tagsTask = ContentService.ListTagsAsync();
        await Task.WhenAll(authorsTask, seriesTask, tagsTask);

        if (authorsTask.Result.IsSuccess)
        {
            authors = authorsTask.Result.Value;
        }

        if (seriesTask.Result.IsSuccess)
        {
            seriesList = seriesTask.Result.Value;
        }

        if (tagsTask.Result.IsSuccess)
        {
            allTags = tagsTask.Result.Value.ToList();
        }
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

    private async Task OnExternalFilesConfirmedAsync(IReadOnlyList<string> paths)
    {
        showAddContentPicker = false;
        addContentBusy = true;
        actionMessage = null;
        actionError = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            var result = await ContentService.RegisterExternalFilesAsync(paths);
            if (result.IsSuccess)
            {
                RegisterExternalFilesResult r = result.Value;
                if (r.Added == 0 && r.Skipped == 0)
                {
                    actionMessage = "No files were registered.";
                }
                else if (r.Added == 0)
                {
                    actionMessage =
                        $"No new files added. {r.Skipped} path(s) skipped (missing, duplicate, or not a file).";
                }
                else
                {
                    actionMessage = r.Skipped > 0
                        ? $"Added {r.Added} file(s). Skipped {r.Skipped} path(s) (already registered or invalid)."
                        : $"Added {r.Added} file(s).";
                }

                await ResetAndLoadAsync();
            }
            else
            {
                actionError = result.Errors.FirstOrDefault() ?? "Could not register files.";
            }
        }
        finally
        {
            addContentBusy = false;
        }
    }

    private async Task OnFiltersChangedAsync() => await ResetAndLoadAsync();

    private async Task OnTagModeChangedAsync()
    {
        if (tagFilterMode != TagFilterMode.Selected)
        {
            selectedTagFilters.Clear();
        }

        await ResetAndLoadAsync();
    }

    private async Task OnTagsSavedAsync()
    {
        tagsModalOpen = false;
        actionMessage = "Tags updated.";
        await ResetAndLoadAsync();
    }

    private void OpenAddContentPicker()
    {
        actionError = null;
        showAddContentPicker = true;
    }

    private void OpenBulkAssign()
    {
        bulkAssignAuthorId = 0;
        bulkAssignError = null;
        bulkAssignOpen = true;
    }

    private void OpenBulkTags()
    {
        if (selectedIds.Count == 0)
        {
            return;
        }

        tagsModalTitle = selectedIds.Count == 1
            ? "Edit tags"
            : $"Edit tags ({selectedIds.Count} items)";
        tagsModalItemIds = selectedIds.ToList();

        if (selectedIds.Count == 1)
        {
            var item = loadedItems.FirstOrDefault(i => i.Id == selectedIds.First());
            tagsModalInitialNames = item?.Tags.Select(t => t.Name).ToList() ?? [];
        }
        else
        {
            tagsModalInitialNames = [];
        }

        tagsModalOpen = true;
    }

    private void OpenSingleAssign(AdditionalContentItemDto item)
    {
        selectedIds.Clear();
        selectedIds.Add(item.Id);
        bulkAssignAuthorId = 0;
        bulkAssignError = null;
        bulkAssignOpen = true;
    }

    private void OpenSingleTags(AdditionalContentItemDto item)
    {
        tagsModalTitle = $"Tags — {item.FileName}";
        tagsModalItemIds = [item.Id];
        tagsModalInitialNames = item.Tags.Select(t => t.Name).ToList();
        tagsModalOpen = true;
    }

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

    private Task<IReadOnlyList<AdditionalContentTagDto>> SearchTagFilterOptionsAsync(string queryText)
    {
        IEnumerable<AdditionalContentTagDto> q = allTags;
        if (!string.IsNullOrWhiteSpace(queryText))
        {
            q = q.Where(t => t.Name.Contains(queryText, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<AdditionalContentTagDto>>(q.Take(20).ToList());
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
}