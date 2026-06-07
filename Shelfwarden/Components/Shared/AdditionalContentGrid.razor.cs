using System.Text;
using Shelfwarden.Models;

namespace Shelfwarden.Components.Shared;

public partial class AdditionalContentGrid : ComponentBase
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    [Parameter, EditorRequired] public IReadOnlyList<AdditionalContentItemDto> Items { get; set; } = [];
    [Parameter] public bool ShowEmptyState { get; set; } = true;
    [Parameter] public EventCallback OnItemDeleted { get; set; }
    [Parameter] public EventCallback<AdditionalContentItemDto> OnItemRenamed { get; set; }
    [Parameter] public EventCallback OnItemsChanged { get; set; }

    /// <summary>When set, tag filter options are limited to tags on this author's extra content.</summary>
    [Parameter] public int? TagScopeAuthorId { get; set; }

    /// <summary><c>0</c> = any, <c>-1</c> = untagged, otherwise a tag id.</summary>
    private int tagFilterId;

    private IReadOnlyList<AdditionalContentItemDto> filteredItems = [];
    private IReadOnlyList<AdditionalContentItemDto> imageItems = [];
    private IReadOnlyList<AdditionalContentItemDto> otherItems = [];
    private string activeTab = "images";

    private List<AdditionalContentTagDto> tagFilterOptions = [];

    private bool tagsModalOpen;
    private string tagsModalTitle = "Edit tags";
    private List<int> tagsModalItemIds = [];
    private List<string> tagsModalInitialNames = [];

    private AdditionalContentItemDto? viewerItem;
    private string? viewerContent;
    private string[][]? viewerCsvRows;
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

    protected override async Task OnParametersSetAsync()
    {
        await LoadTagFilterOptionsAsync();
        RefreshFilteredLists();
    }

    private async Task LoadTagFilterOptionsAsync()
    {
        if (TagScopeAuthorId is int authorId)
        {
            var tagsResult = await ContentService.ListTagsForAuthorAsync(authorId);
            if (tagsResult.IsSuccess)
            {
                tagFilterOptions = tagsResult.Value.ToList();
            }
        }
        else
        {
            tagFilterOptions = Items
                .SelectMany(i => i.Tags)
                .DistinctBy(t => t.Id)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private void RefreshFilteredLists()
    {
        filteredItems = ApplyTagFilter(Items).ToList();

        imageItems = filteredItems
            .Where(i => IsImage(i.FileExtension))
            .OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        otherItems = filteredItems
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

    private void OnTagFilterChanged() => RefreshFilteredLists();

    private IEnumerable<AdditionalContentItemDto> ApplyTagFilter(IReadOnlyList<AdditionalContentItemDto> source)
    {
        if (tagFilterId == 0)
        {
            return source;
        }

        if (tagFilterId == -1)
        {
            return source.Where(i => i.Tags.Count == 0);
        }

        return source.Where(i => i.Tags.Any(t => t.Id == tagFilterId));
    }

    private void ActivateTab(string tab) => activeTab = tab;

    private void OpenTagsModal(AdditionalContentItemDto item)
    {
        tagsModalTitle = $"Tags — {item.FileName}";
        tagsModalItemIds = [item.Id];
        tagsModalInitialNames = item.Tags.Select(t => t.Name).ToList();
        tagsModalOpen = true;
    }

    private void CloseTagsModal() => tagsModalOpen = false;

    private async Task OnTagsSavedAsync()
    {
        tagsModalOpen = false;
        await OnItemsChanged.InvokeAsync();
    }

    private async Task OpenViewer(AdditionalContentItemDto item)
    {
        viewerItem = item;
        viewerContent = null;
        viewerCsvRows = null;

        if (IsImage(item.FileExtension) || IsVideo(item.FileExtension))
        {
            return;
        }

        viewerLoading = true;
        StateHasChanged();

        try
        {
            var result = await ContentService.GetViewableTextAsync(item.Id);
            if (result.IsSuccess)
            {
                viewerContent = result.Value;
                if (IsCsv(item.FileExtension))
                {
                    viewerCsvRows = ParseCsv(viewerContent);
                }
            }
            else
            {
                viewerContent = result.Errors.FirstOrDefault() ?? "(Unable to load content)";
            }
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
        viewerCsvRows = null;
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

    private static bool IsCsv(string ext) => ext == ".csv";

    private static bool IsVideo(string ext) => ext is ".mp4" or ".mkv";

    private static bool IsViewable(string ext) =>
        IsImage(ext) || IsText(ext) || IsHtml(ext) || IsCsv(ext) || IsVideo(ext);

    private static string GetFileIcon(string ext) => ext switch
    {
        ".pdf" => "bi-filetype-pdf",
        ".txt" => "bi-filetype-txt",
        ".md" => "bi-markdown",
        ".html" or ".htm" => "bi-filetype-html",
        ".csv" => "bi-filetype-csv",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" => "bi-image",
        ".zip" => "bi-file-zip",
        ".mp3" => "bi-music-note",
        ".mp4" or ".mkv" => "bi-camera-video",
        _ => "bi-file-earmark",
    };

    private static string[][]? ParseCsv(string content)
    {
        var rows = new List<string[]>();
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            rows.Add(ParseCsvLine(line));
        }

        return rows.Count == 0 ? null : rows.ToArray();
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
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

    private static string GetVideoMimeType(string ext) => ext switch
    {
        ".mkv" => "video/x-matroska",
        _ => "video/mp4",
    };

    /// <summary>Full-page request so Blazor does not treat the download URL as in-app navigation.</summary>
    private void DownloadItem(int id) =>
        Navigation.NavigateTo($"extra-content/{id}?download=true", forceLoad: true);
}
