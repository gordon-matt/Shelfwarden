namespace Shelfwarden.Components.Pages;

public partial class Collections : ComponentBase
{
    private bool busy;
    private IReadOnlyList<CollectionDto>? collections;
    private bool creating;
    private string? errorMessage;
    private CreateModel newCollection = new();
    private string shelfFilterId = string.Empty;
    private IReadOnlyList<ShelfDto> shelves = [];

    protected override async Task OnInitializedAsync()
    {
        var shelvesResult = await ShelfService.GetAllAsync();
        if (shelvesResult.IsSuccess)
        {
            shelves = shelvesResult.Value;
        }

        await LoadAsync();
    }

    private void CancelCreate()
    {
        creating = false;
        errorMessage = null;
    }

    private async Task ClearShelfFilterAsync()
    {
        shelfFilterId = "";
        await LoadAsync();
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

    private async Task LoadAsync()
    {
        int? shelfId = int.TryParse(shelfFilterId, out int sid) && sid > 0 ? sid : null;
        var result = await CollectionService.ListAsync(shelfId);
        collections = result.IsSuccess ? result.Value : [];
    }

    private async Task OnShelfFilterChangedAsync() => await LoadAsync();

    private void ShowCreate()
    {
        creating = true;
        errorMessage = null;
        newCollection = new CreateModel();
    }

    private sealed class CreateModel
    {
        public string? Description { get; set; }

        public bool IsGlobal { get; set; }

        [Required, StringLength(256)]
        public string Name { get; set; } = string.Empty;
    }
}