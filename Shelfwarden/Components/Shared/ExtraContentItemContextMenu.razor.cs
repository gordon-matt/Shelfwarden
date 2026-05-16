using Shelfwarden.Models;

namespace Shelfwarden.Components.Shared;

public partial class ExtraContentItemContextMenu : ComponentBase
{
    [Parameter, EditorRequired] public AdditionalContentItemDto Item { get; set; } = null!;

    /// <summary>Pin menu on cover/thumb (<c>extra-content-item-menu--cover</c>) or inline on a row.</summary>
    [Parameter] public string MenuClass { get; set; } = "extra-content-item-menu--cover";

    /// <summary>Admin extra-content page: assign + tags (page is administrator-only).</summary>
    [Parameter] public bool ShowAssign { get; set; }

    [Parameter] public EventCallback OnView { get; set; }
    [Parameter] public EventCallback OnTags { get; set; }
    [Parameter] public EventCallback OnAssign { get; set; }
    [Parameter] public EventCallback OnRename { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }

    private bool ShowView =>
        IsViewable(Item.FileExtension) && OnView.HasDelegate;

    private bool ShowOpenPdf =>
        Item.FileExtension == ".pdf" && !ShowView;

    private Task OnViewClick() => OnView.InvokeAsync();
    private Task OnTagsClick() => OnTags.InvokeAsync();
    private Task OnAssignClick() => OnAssign.InvokeAsync();
    private Task OnRenameClick() => OnRename.InvokeAsync();
    private Task OnDeleteClick() => OnDelete.InvokeAsync();

    private static bool IsViewable(string ext) =>
        ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg"
            or ".txt" or ".md" or ".html" or ".htm";
}
