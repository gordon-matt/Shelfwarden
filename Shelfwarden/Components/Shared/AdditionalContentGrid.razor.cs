namespace Shelfwarden.Components.Shared;

public partial class AdditionalContentGrid : ComponentBase
{
    [Parameter, EditorRequired] public IReadOnlyList<AdditionalContentItemDto> Items { get; set; } = [];
    [Parameter] public bool ShowEmptyState { get; set; } = true;
    [Parameter] public EventCallback OnItemDeleted { get; set; }
    [Parameter] public EventCallback<AdditionalContentItemDto> OnItemRenamed { get; set; }

    private IReadOnlyList<AdditionalContentItemDto> imageItems = [];
    private IReadOnlyList<AdditionalContentItemDto> otherItems = [];
    private string activeTab = "images";

    private AdditionalContentItemDto? viewerItem;
    private string? viewerContent;
    private bool viewerLoading;

    private AdditionalContentItemDto? renameItem;
    private string renameName = string.Empty;
    private bool renameSaving;
    private string? renameError;

    private bool ShowTabs => imageItems.Count > 0 && otherItems.Count > 0;

    private bool ShowImageGallery =>
        imageItems.Count > 0 && (!ShowTabs || activeTab == "images");

    private bool ShowOtherList =>
        otherItems.Count > 0 && (!ShowTabs || activeTab == "other");

    private string ImagesMasonryKey => string.Join(',', imageItems.Select(i => i.Id));

    protected override void OnParametersSet()
    {
        imageItems = Items
            .Where(i => IsImage(i.FileExtension))
            .OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        otherItems = Items
            .Where(i => !IsImage(i.FileExtension))
            .OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ShowTabs)
        {
            if (activeTab == "images" && imageItems.Count == 0)
            {
                activeTab = "other";
            }
            else if (activeTab == "other" && otherItems.Count == 0)
            {
                activeTab = "images";
            }
        }
    }

    private void ActivateTab(string tab)
    {
        activeTab = tab;
    }

    private async Task OpenViewer(AdditionalContentItemDto item)
    {
        viewerItem = item;
        viewerContent = null;

        if (IsImage(item.FileExtension))
        {
            return;
        }

        viewerLoading = true;
        StateHasChanged();

        try
        {
            // Fetch the text content from the controller endpoint.
            using var http = new System.Net.Http.HttpClient();
            viewerContent = await http.GetStringAsync($"extra-content/{item.Id}");
        }
        catch
        {
            viewerContent = "(Unable to load content)";
        }
        finally
        {
            viewerLoading = false;
        }
    }

    private void CloseViewer()
    {
        viewerItem = null;
        viewerContent = null;
    }

    private void OpenRename(AdditionalContentItemDto item)
    {
        renameItem = item;
        renameName = item.FileName;
        renameError = null;
    }

    private void CloseRename()
    {
        renameItem = null;
        renameError = null;
    }

    private async Task SaveRenameAsync()
    {
        if (renameItem is null || string.IsNullOrWhiteSpace(renameName))
        {
            return;
        }

        renameSaving = true;
        renameError = null;
        try
        {
            var result = await ContentService.RenameAsync(new RenameContentItemRequest
            {
                ItemId = renameItem.Id,
                NewFileName = renameName.Trim(),
            });

            if (!result.IsSuccess)
            {
                renameError = result.Errors.FirstOrDefault() ?? "Could not rename item.";
                return;
            }

            var renamed = renameItem with { FileName = renameName.Trim() };
            renameItem = null;
            await OnItemRenamed.InvokeAsync(renamed);
        }
        finally
        {
            renameSaving = false;
        }
    }

    private async Task ConfirmDeleteAsync(AdditionalContentItemDto item)
    {
        if (!await JS.InvokeAsync<bool>("shelfwarden.confirmDialog",
                $"Delete '{item.FileName}'? The file will be permanently removed from disk."))
        {
            return;
        }

        var result = await ContentService.DeleteAsync([item.Id]);
        if (result.IsSuccess)
        {
            await OnItemDeleted.InvokeAsync();
        }
    }

    private static bool IsImage(string ext) =>
        ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg";

    private static bool IsPdf(string ext) => ext == ".pdf";

    private static bool IsHtml(string ext) => ext is ".html" or ".htm";

    private static bool IsText(string ext) => ext is ".txt" or ".md";

    private static bool IsViewable(string ext) => IsImage(ext) || IsText(ext) || IsHtml(ext);

    private static string GetFileIcon(string ext) => ext switch
    {
        ".pdf" => "bi-filetype-pdf",
        ".txt" => "bi-filetype-txt",
        ".md" => "bi-markdown",
        ".html" or ".htm" => "bi-filetype-html",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" => "bi-image",
        ".zip" => "bi-file-zip",
        ".mp3" => "bi-music-note",
        ".mp4" or ".mkv" => "bi-camera-video",
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
