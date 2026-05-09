using Microsoft.AspNetCore.Components.Authorization;

namespace Shelfwarden.Components.Shared;

public partial class BulkBookActions : ComponentBase
{
    [Parameter, EditorRequired]
    public IReadOnlyCollection<int> SelectedBookIds { get; set; } = [];

    private bool collectionModalOpen;
    private bool readingListModalOpen;
    private bool busy;
    private bool isAdministrator;
    private string? collectionsLoadError;
    private string? readingListsLoadError;
    private string? bulkActionError;

    private IReadOnlyList<ReadingListDto> readingLists = [];
    private IReadOnlyList<CollectionDto> writableCollections = [];

    private int selectedCollectionId;
    private int selectedReadingListId;

    protected override async Task OnInitializedAsync()
    {
        var auth = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        isAdministrator = auth.User.IsInRole(Constants.Roles.Administrator);
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

    private void CloseCollectionModal()
    {
        collectionModalOpen = false;
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
            int ok = 0;
            int failed = 0;
            foreach (int bookId in SelectedBookIds)
            {
                var r = await CollectionService.AddBookAsync(selectedCollectionId, bookId);
                if (r.IsSuccess)
                {
                    ok++;
                }
                else
                {
                    failed++;
                }
            }

            if (failed > 0 && ok == 0)
            {
                bulkActionError = "Could not add books to this collection. You may not have permission.";
            }
            else
            {
                collectionModalOpen = false;
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

    private void CloseReadingListModal()
    {
        readingListModalOpen = false;
        bulkActionError = null;
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
            int ok = 0;
            int failed = 0;
            foreach (int bookId in SelectedBookIds)
            {
                var r = await ReadingListService.AddBookAsync(selectedReadingListId, bookId);
                if (r.IsSuccess)
                {
                    ok++;
                }
                else
                {
                    failed++;
                }
            }

            if (failed > 0 && ok == 0)
            {
                bulkActionError = "Could not add books to this list.";
            }
            else
            {
                readingListModalOpen = false;
            }
        }
        finally
        {
            busy = false;
        }
    }
}