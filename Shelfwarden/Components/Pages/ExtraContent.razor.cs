namespace Shelfwarden.Components.Pages;

public partial class ExtraContent : ComponentBase
{
    private IReadOnlyList<AdditionalContentItemDto>? items;
    private List<AdditionalContentItemDto> filteredItems = [];
    private IReadOnlyList<AuthorListItemDto>? authors;
    private IReadOnlyList<SeriesListItemDto>? seriesList;

    private int authorFilter;
    private int seriesFilter;

    private readonly HashSet<int> selectedIds = [];
    private bool allSelected;

    private bool scanning;
    private string? scanMessage;
    private string? actionMessage;
    private string? actionError;

    private bool bulkActionBusy;
    private bool bulkAssignOpen;
    private int bulkAssignAuthorId;
    private string? bulkAssignError;

    private AdditionalContentItemDto? singleAssignItem;

    protected override async Task OnInitializedAsync()
    {
        await Task.WhenAll(LoadItemsAsync(), LoadFiltersAsync());
    }

    private async Task LoadItemsAsync()
    {
        var result = await ContentService.ListAsync();
        if (result.IsSuccess)
        {
            items = result.Value;
            ApplyFilter();
        }
    }

    private async Task LoadFiltersAsync()
    {
        var authorsTask = AuthorService.ListAsync();
        var seriesTask = SeriesService.ListAsync();
        await Task.WhenAll(authorsTask, seriesTask);

        if (authorsTask.Result.IsSuccess) authors = authorsTask.Result.Value;
        if (seriesTask.Result.IsSuccess) seriesList = seriesTask.Result.Value;
    }

    private void ApplyFilter()
    {
        if (items is null)
        {
            filteredItems = [];
            return;
        }

        IEnumerable<AdditionalContentItemDto> query = items;

        // Author filter: 0 = Any, -1 = None (unassigned), positive = specific author
        if (authorFilter != 0)
        {
            query = authorFilter == -1
                ? query.Where(i => i.AuthorId is null)
                : query.Where(i => i.AuthorId == authorFilter);
        }

        // Series filter: 0 = Any, -1 = None, positive = specific series
        if (seriesFilter != 0)
        {
            query = seriesFilter == -1
                ? query.Where(i => i.Series.Count == 0)
                : query.Where(i => i.Series.Any(s => s.Id == seriesFilter));
        }

        filteredItems = query.ToList();

        // Clear selections that are no longer visible.
        selectedIds.RemoveWhere(id => !filteredItems.Any(i => i.Id == id));
        allSelected = filteredItems.Count > 0 && filteredItems.All(i => selectedIds.Contains(i.Id));
    }

    private void ToggleItem(int id, bool include)
    {
        if (include) selectedIds.Add(id);
        else selectedIds.Remove(id);

        allSelected = filteredItems.Count > 0 && filteredItems.All(i => selectedIds.Contains(i.Id));
    }

    private void ToggleSelectAll()
    {
        if (allSelected)
        {
            foreach (var item in filteredItems)
            {
                selectedIds.Add(item.Id);
            }
        }
        else
        {
            selectedIds.Clear();
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
                await LoadItemsAsync();
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
        if (selectedIds.Count == 0) return;

        bulkActionBusy = true;
        actionError = null;
        try
        {
            var result = await ContentService.DeleteAsync(selectedIds.ToList());
            if (result.IsSuccess)
            {
                actionMessage = $"Deleted {selectedIds.Count} item(s).";
                selectedIds.Clear();
                await LoadItemsAsync();
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
        if (bulkAssignAuthorId == 0 || selectedIds.Count == 0) return;

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
                await LoadItemsAsync();
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
}
