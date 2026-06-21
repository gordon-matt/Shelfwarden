namespace Shelfwarden.Components.Shared;

public partial class ExtraContentItemContextMenu : ComponentBase
{
    [Parameter, EditorRequired] public AdditionalContentItemDto Item { get; set; } = null!;

    /// <summary>Pin menu on cover/thumb (<c>extra-content-item-menu--cover</c>) or inline on a row.</summary>
    [Parameter] public string MenuClass { get; set; } = "extra-content-item-menu--cover";

    [Parameter] public EventCallback OnAssign { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback OnRename { get; set; }
    [Parameter] public EventCallback OnTags { get; set; }
    [Parameter] public EventCallback OnView { get; set; }

    /// <summary>Admin extra-content page: assign + tags (page is administrator-only).</summary>
    [Parameter] public bool ShowAssign { get; set; }

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private bool ShowOpenPdf => Item.FileExtension == ".pdf" && !ShowView;

    private bool ShowView => IsViewable(Item.FileExtension) && OnView.HasDelegate;

    private static bool IsViewable(string ext) =>
        ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg"
            or ".txt" or ".md" or ".html" or ".htm" or ".csv"
            or ".mp4" or ".mkv";

    private Task DownloadAsync()
    {
        Navigation.NavigateTo($"extra-content/{Item.Id}?download=true", forceLoad: true);
        return Task.CompletedTask;
    }

    private Task OnAssignClick() => OnAssign.InvokeAsync();

    private Task OnDeleteClick() => OnDelete.InvokeAsync();

    private Task OnRenameClick() => OnRename.InvokeAsync();

    private Task OnTagsClick() => OnTags.InvokeAsync();

    private Task OnViewClick() => OnView.InvokeAsync();
}