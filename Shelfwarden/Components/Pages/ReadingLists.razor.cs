namespace Shelfwarden.Components.Pages;

public partial class ReadingLists : ComponentBase
{
    private bool busy;
    private bool creating;
    private string? errorMessage;
    private IReadOnlyList<ReadingListDto>? lists;
    private CreateModel newList = new();
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
            var result = await ReadingListService.CreateAsync(new CreateReadingListRequest
            {
                Name = newList.Name,
                Description = newList.Description,
            });
            if (result.IsSuccess)
            {
                creating = false;
                SidebarNavRefresh.NotifyNavigationDataChanged();
                NavigationManager.NavigateTo($"reading-lists/{result.Value.Id}");
            }
            else
            {
                errorMessage = result.Errors.FirstOrDefault() ?? "Could not create list.";
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
        var result = await ReadingListService.ListAsync(shelfId);
        lists = result.IsSuccess ? result.Value : [];
    }

    private async Task OnShelfFilterChangedAsync() => await LoadAsync();

    private void ShowCreate()
    {
        creating = true;
        errorMessage = null;
        newList = new CreateModel();
    }

    private sealed class CreateModel
    {
        public string? Description { get; set; }

        [Required, StringLength(256)]
        public string Name { get; set; } = string.Empty;
    }
}