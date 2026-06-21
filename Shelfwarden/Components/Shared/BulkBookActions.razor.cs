using Microsoft.AspNetCore.Components.Authorization;

namespace Shelfwarden.Components.Shared;

public partial class BulkBookActions : ComponentBase
{
    private string? bulkActionError;
    private bool busy;
    private bool collectionModalOpen;
    private string? collectionsLoadError;
    private bool isAdministrator;
    private bool readingListModalOpen;
    private IReadOnlyList<ReadingListDto> readingLists = [];
    private string? readingListsLoadError;
    private int selectedCollectionId;
    private int selectedReadingListId;
    private IReadOnlyList<CollectionDto> writableCollections = [];

    [Parameter, EditorRequired]
    public IReadOnlyCollection<int> SelectedBookIds { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var auth = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        isAdministrator = auth.User.IsInRole(Constants.Roles.Administrator);
    }

    private void CloseCollectionModal()
    {
        collectionModalOpen = false;
        bulkActionError = null;
    }

    private void CloseReadingListModal()
    {
        readingListModalOpen = false;
        bulkActionError = null;
    }

    private async Task ConfirmAddToCollectionAsync()
    {
        if (writableCollections.Count == 0 || SelectedBookIds.Count == 0)
        {
            return;
        }

        bulkActionError = null;
        busy = true;
        try
        {
            var result = await CollectionService.AddBooksAsync(selectedCollectionId, [.. SelectedBookIds]);
            if (result.IsSuccess)
            {
                collectionModalOpen = false;
            }
            else
            {
                bulkActionError = "Could not add books to this collection. You may not have permission.";
            }
        }
        finally
        {
            busy = false;
        }
    }

    private async Task ConfirmAddToReadingListAsync()
    {
        if (readingLists.Count == 0 || SelectedBookIds.Count == 0)
        {
            return;
        }

        bulkActionError = null;
        busy = true;
        try
        {
            var result = await ReadingListService.AddBooksAsync(selectedReadingListId, [.. SelectedBookIds]);
            if (result.IsSuccess)
            {
                readingListModalOpen = false;
            }
            else
            {
                bulkActionError = "Could not add books to this list.";
            }
        }
        finally
        {
            busy = false;
        }
    }

    private async Task OpenCollectionModalAsync()
    {
        bulkActionError = null;
        collectionsLoadError = null;
        collectionModalOpen = true;
        busy = true;
        try
        {
            var result = await CollectionService.ListAsync();
            if (result.IsSuccess)
            {
                writableCollections = result.Value
                    .Where(c => !c.IsGlobal || isAdministrator)
                    .ToList();
                selectedCollectionId = writableCollections.FirstOrDefault()?.Id ?? 0;
            }
            else
            {
                collectionsLoadError = result.Errors.FirstOrDefault() ?? "Could not load collections.";
            }
        }
        finally
        {
            busy = false;
        }
    }

    private async Task OpenReadingListModalAsync()
    {
        bulkActionError = null;
        readingListsLoadError = null;
        readingListModalOpen = true;
        busy = true;
        try
        {
            var result = await ReadingListService.ListAsync();
            if (result.IsSuccess)
            {
                readingLists = result.Value;
                selectedReadingListId = readingLists.FirstOrDefault()?.Id ?? 0;
            }
            else
            {
                readingListsLoadError = result.Errors.FirstOrDefault() ?? "Could not load reading lists.";
            }
        }
        finally
        {
            busy = false;
        }
    }
}