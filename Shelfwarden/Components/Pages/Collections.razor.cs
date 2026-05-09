namespace Shelfwarden.Components.Pages;

public partial class Collections : ComponentBase
{
    private IReadOnlyList<CollectionDto>? collections;
    private IReadOnlyList<ShelfDto> shelves = [];
    private string shelfFilterId = "";
    private bool creating;
    private bool busy;
    private string? errorMessage;
    private CreateModel newCollection = new();

    protected override async Task OnInitializedAsync()
    {
        var shelvesResult = await ShelfService.GetAllAsync();
        if (shelvesResult.IsSuccess)
        {
            shelves = shelvesResult.Value;
        }

        await LoadAsync();
    }

    private async Task OnShelfFilterChangedAsync() => await LoadAsync();

    private async Task ClearShelfFilterAsync()
    {
        shelfFilterId = "";
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        int? shelfId = int.TryParse(shelfFilterId, out int sid) && sid > 0 ? sid : null;
        var result = await CollectionService.ListAsync(shelfId);
        collections = result.IsSuccess ? result.Value : [];
    }

    private void ShowCreate()
    {
        creating = true;
        errorMessage = null;
        newCollection = new CreateModel();
    }

    private void CancelCreate()
    {
        creating = false;
        errorMessage = null;
    }

    private async Task CreateAsync()
    {
        busy = true;
        errorMessage = null;
        try
        {
            var result = await CollectionService.CreateAsync(new CreateCollectionRequest
            {
                Name = newCollection.Name,
                Description = newCollection.Description,
                IsGlobal = newCollection.IsGlobal,
            });
            if (result.IsSuccess)
            {
                creating = false;
                SidebarNavRefresh.NotifyNavigationDataChanged();
                NavigationManager.NavigateTo($"collections/{result.Value.Id}");
            }
            else
            {
                errorMessage = result.Errors.FirstOrDefault() ?? "Could not create collection.";
            }
        }
        finally
        {
            busy = false;
        }
    }

    private sealed class CreateModel
    {
        [Required, StringLength(256)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public bool IsGlobal { get; set; }
    }
}