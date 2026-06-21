namespace Shelfwarden.Components.Pages;

public partial class CollectionDetail : ComponentBase
{
    private readonly List<BookListItemDto> bannerSelectedBooks = [];
    private readonly HashSet<int> selectedBookIds = [];
    private CardHeaderBannerMode bannerMode = CardHeaderBannerMode.RandomCovers;
    private bool canModify;
    private CollectionDetailDto? collection;
    private bool editing;
    private EditModel editModel = new();
    private bool isAdministrator => isAdministrator;
    private bool loading = true;
    private bool savingEdit;
    private CancellationTokenSource? searchCts;
    private string? searchInput;
    private IReadOnlyList<BookListItemDto> searchResults = [];
    private bool showSearch;
    private string? startsWithFilter;

    [Parameter]
    public int Id { get; set; }

    private int BulkEditMaxBookCount => Math.Max(1, Configuration.GetValue<int?>("BulkEditMaxBookCount") ?? 100);

    private bool CanSelectAllMatching => MatchingBooksCount > 0 && MatchingBooksCount <= BulkEditMaxBookCount;

    private IReadOnlyList<BookListItemDto> FilteredBooks =>
        collection?.Books.Where(b => BookListLetterFilter.MatchesTitleOrSortTitle(b, startsWithFilter)).ToList() ?? [];

    private int MatchingBooksCount =>
        collection?.Books.Count(b => BookListLetterFilter.MatchesTitleOrSortTitle(b, startsWithFilter)) ?? 0;

    protected override async Task OnParametersSetAsync()
    {
        loading = true;
        await LoadAsync();
        loading = false;
    }

    private async Task AddBookAsync(BookListItemDto book)
    {
        var result = await CollectionService.AddBookAsync(Id, book.Id);
        if (result.IsSuccess)
        {
            searchInput = string.Empty;
            searchResults = [];
            showSearch = false;
            await LoadAsync();
        }
    }

    private void ClearLetterFilter() => startsWithFilter = null;

    private void ClearSelection() => selectedBookIds.Clear();

    private async Task DeleteAsync()
    {
        var result = await CollectionService.DeleteAsync(Id);
        if (result.IsSuccess)
        {
            SidebarNavRefresh.NotifyNavigationDataChanged();
            NavigationManager.NavigateTo("collections");
        }
    }

    private void GoToBatchEdit()
    {
        if (selectedBookIds.Count == 0)
        {
            return;
        }

        string ids = string.Join(',', selectedBookIds.Order());
        NavigationManager.NavigateTo($"books/batch-edit?ids={ids}&return=collections/{Id}");
    }

    private async Task LoadAsync()
    {
        var result = await CollectionService.GetByIdAsync(Id);
        if (result.IsSuccess)
        {
            collection = result.Value;
            selectedBookIds.Clear();
            editModel = new EditModel
            {
                Name = collection.Name,
                Description = collection.Description,
                IsGlobal = collection.IsGlobal,
            };
            bannerMode = collection.BannerSettings.Mode;
            bannerSelectedBooks.Clear();
            foreach (int bid in collection.BannerSettings.SelectedBookIds)
            {
                var b = collection.Books.FirstOrDefault(x => x.Id == bid);
                if (b is not null)
                {
                    bannerSelectedBooks.Add(b);
                }
            }
            string? userId = UserContext.GetCurrentUserId();
            // Owner can always modify; admins can modify the global flavour. Personal
            // collections owned by other users are 403'd by the service before we get here.
            canModify = collection.IsGlobal
                ? isAdministrator
                : collection.OwnerUserId == userId;
        }
        else
        {
            collection = null;
        }
    }

    private async Task OnSearchInput(ChangeEventArgs e)
    {
        searchInput = e.Value?.ToString();
        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;
        try
        {
            await Task.Delay(200, token);
            if (string.IsNullOrWhiteSpace(searchInput))
            {
                searchResults = [];
                showSearch = false;
                return;
            }

            var result = await BookService.SearchAsync(new BookSearchRequest
            {
                Query = searchInput,
                Page = 1,
                PageSize = 8,
            }, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            // Filter out books already in the collection so the user doesn't try to re-add them.
            var existing = collection?.Books.Select(b => b.Id).ToHashSet() ?? [];
            searchResults = result.IsSuccess
                ? result.Value.Items.Where(b => !existing.Contains(b.Id)).ToList()
                : [];
            showSearch = true;
            await InvokeAsync(StateHasChanged);
        }
        catch (TaskCanceledException) { }
    }

    private async Task RemoveBookAsync(int bookId)
    {
        var result = await CollectionService.RemoveBookAsync(Id, bookId);
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private async Task SaveAsync()
    {
        savingEdit = true;
        try
        {
            var result = await CollectionService.UpdateAsync(Id, new UpdateCollectionRequest
            {
                Name = editModel.Name,
                Description = editModel.Description,
                IsGlobal = isAdministrator && editModel.IsGlobal,
                CardBannerMode = bannerMode,
                CardBannerSelectedBookIds = bannerSelectedBooks.Select(b => b.Id).ToList(),
            });
            if (result.IsSuccess)
            {
                editing = false;
                SidebarNavRefresh.NotifyNavigationDataChanged();
                await LoadAsync();
            }
        }
        finally
        {
            savingEdit = false;
        }
    }

    private async Task<IReadOnlyList<BookListItemDto>> SearchBooksInCollectionForBannerAsync(string query)
    {
        var result = await BookService.SearchAsync(new BookSearchRequest
        {
            CollectionId = Id,
            Query = string.IsNullOrWhiteSpace(query) ? null : query,
            Page = 1,
            PageSize = 20,
        });
        return result.IsSuccess ? result.Value.Items : [];
    }

    private void SelectAllMatching()
    {
        if (collection is null || !CanSelectAllMatching)
        {
            return;
        }

        foreach (var b in collection.Books.Where(b => BookListLetterFilter.MatchesTitleOrSortTitle(b, startsWithFilter)))
        {
            selectedBookIds.Add(b.Id);
        }
    }

    private void SelectAllVisible()
    {
        foreach (var b in FilteredBooks)
        {
            selectedBookIds.Add(b.Id);
        }
    }

    private void ToggleSelection(int bookId, bool include)
    {
        if (include)
        {
            selectedBookIds.Add(bookId);
        }
        else
        {
            selectedBookIds.Remove(bookId);
        }
    }

    private async Task UploadCollectionBannerAsync(IBrowserFile file)
    {
        await using var s = file.OpenReadStream(2_000_000);
        var result = await CollectionService.UploadCardBannerAsync(Id, s, file.Name, file.Size);
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private sealed class EditModel
    {
        public string? Description { get; set; }

        public bool IsGlobal { get; set; }

        [Required, StringLength(256)]
        public string Name { get; set; } = string.Empty;
    }
}