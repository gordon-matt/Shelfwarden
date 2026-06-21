namespace Shelfwarden.Components.Shared;

public partial class AssignContentModal : ComponentBase
{
    private readonly HashSet<int> selectedIds = [];
    private IReadOnlyList<AdditionalContentItemDto>? allItems;
    private string? error;
    private List<AdditionalContentItemDto> filteredItems = [];
    private bool filterUnassigned;
    private bool loading;
    private bool saving;

    /// <summary>
    /// The author whose content library is shown. Used when no <see cref="SourceSeriesId"/> is set.
    /// </summary>
    [Parameter] public int AuthorId { get; set; }

    /// <summary>The database id of the book or series being assigned to.</summary>
    [Parameter] public int EntityId { get; set; }

    /// <summary>Display label for the entity type (e.g. "series", "book").</summary>
    [Parameter] public string EntityLabel { get; set; } = "entity";

    /// <summary>Display name of the specific entity (e.g. the series name).</summary>
    [Parameter] public string? EntityName { get; set; }

    /// <summary>
    /// Which entity type is being assigned to: "book" or "series". Determines which service
    /// method is called when the user confirms.
    /// </summary>
    [Parameter] public string EntityType { get; set; } = "book";

    /// <summary>Whether the modal is currently open.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Called after a successful association so the parent can refresh.</summary>
    [Parameter] public EventCallback OnAssociated { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// When set, the modal loads content already assigned to this series instead of a specific
    /// author. Used on the SeriesDetail page so book-assignment only offers items tied to the
    /// series.
    /// </summary>
    [Parameter] public int? SourceSeriesId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && allItems is null)
        {
            await LoadItemsAsync();
        }
        else if (!IsOpen)
        {
            allItems = null;
            filteredItems = [];
            selectedIds.Clear();
            filterUnassigned = false;
            error = null;
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

    private void ApplyFilter()
    {
        if (allItems is null)
        {
            filteredItems = [];
            return;
        }

        filteredItems = filterUnassigned
            ? allItems.Where(i => i.Books.Count == 0 && i.Series.Count == 0).ToList()
            : allItems.ToList();

        // Remove items already associated with this entity.
        if (EntityType == "book")
        {
            filteredItems = filteredItems
                .Where(i => !i.Books.Any(b => b.Id == EntityId))
                .ToList();
        }
        else if (EntityType == "series")
        {
            filteredItems = filteredItems
                .Where(i => !i.Series.Any(s => s.Id == EntityId))
                .ToList();
        }
    }

    private async Task AssociateSelectedAsync()
    {
        if (selectedIds.Count == 0) return;

        saving = true;
        error = null;
        try
        {
            foreach (int itemId in selectedIds)
            {
                Result result;
                if (EntityType == "series")
                {
                    result = await ContentService.AssociateWithSeriesAsync(new AssociateContentRequest
                    {
                        ItemId = itemId,
                        Ids = [EntityId],
                    });
                }
                else
                {
                    result = await ContentService.AssociateWithBooksAsync(new AssociateContentRequest
                    {
                        ItemId = itemId,
                        Ids = [EntityId],
                    });
                }

                if (!result.IsSuccess)
                {
                    error = result.Errors.FirstOrDefault() ?? "Could not associate content.";
                    return;
                }
            }

            await OnAssociated.InvokeAsync();
            await OnClose.InvokeAsync();
        }
        finally
        {
            saving = false;
        }
    }

    private async Task Cancel() => await OnClose.InvokeAsync();

    private async Task LoadItemsAsync()
    {
        loading = true;
        error = null;
        try
        {
            Result<IReadOnlyList<AdditionalContentItemDto>> result = SourceSeriesId.HasValue
                ? await ContentService.GetForSeriesAsync(SourceSeriesId.Value)
                : await ContentService.GetForAuthorAsync(AuthorId);

            if (result.IsSuccess)
            {
                allItems = result.Value;
                ApplyFilter();
            }
            else
            {
                error = "Could not load content items.";
            }
        }
        finally
        {
            loading = false;
        }
    }

    private void ToggleItem(int id, bool include)
    {
        if (include) selectedIds.Add(id);
        else selectedIds.Remove(id);
    }
}