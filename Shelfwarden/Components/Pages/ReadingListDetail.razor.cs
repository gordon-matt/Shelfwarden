namespace Shelfwarden.Components.Pages;

public partial class ReadingListDetail : ComponentBase
{
    [Parameter]
    public int Id { get; set; }

    private ReadingListDetailDto? list;
    private bool loading = true;
    private bool editing;
    private bool savingEdit;
    private bool reordering;
    private EditModel editModel = new();

    private CardHeaderBannerMode bannerMode = CardHeaderBannerMode.RandomCovers;
    private readonly List<BookListItemDto> bannerSelectedBooks = [];

    private string? searchInput;
    private bool showSearch;
    private IReadOnlyList<BookListItemDto> searchResults = [];
    private CancellationTokenSource? searchCts;

    /// <summary>Id of the next book to read — first item that's not at 100% progress, or null when nothing remains.</summary>
    private int? firstUnreadId;

    protected override async Task OnParametersSetAsync()
    {
        loading = true;
        await LoadAsync();
        loading = false;
    }

    private async Task LoadAsync()
    {
        var result = await ReadingListService.GetByIdAsync(Id);
        if (result.IsSuccess)
        {
            list = result.Value;
            editModel = new EditModel { Name = list.Name, Description = list.Description };
            bannerMode = list.BannerSettings.Mode;
            bannerSelectedBooks.Clear();
            foreach (int bid in list.BannerSettings.SelectedBookIds)
            {
                var hit = list.Items.FirstOrDefault(i => i.Book.Id == bid);
                if (hit is not null)
                {
                    bannerSelectedBooks.Add(hit.Book);
                }
            }

            firstUnreadId = list.Items
                .FirstOrDefault(i => i.Book.ProgressPercentage < 100)?.Book.Id;
        }
        else
        {
            list = null;
        }
    }

    private async Task SaveAsync()
    {
        savingEdit = true;
        try
        {
            var result = await ReadingListService.UpdateAsync(Id, new UpdateReadingListRequest
            {
                Name = editModel.Name,
                Description = editModel.Description,
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

    private async Task DeleteAsync()
    {
        var result = await ReadingListService.DeleteAsync(Id);
        if (result.IsSuccess)
        {
            SidebarNavRefresh.NotifyNavigationDataChanged();
            NavigationManager.NavigateTo("reading-lists");
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

            var existing = list?.Items.Select(i => i.Book.Id).ToHashSet() ?? [];
            searchResults = result.IsSuccess
                ? result.Value.Items.Where(b => !existing.Contains(b.Id)).ToList()
                : [];
            showSearch = true;
            await InvokeAsync(StateHasChanged);
        }
        catch (TaskCanceledException) { }
    }

    private async Task AddBookAsync(BookListItemDto book)
    {
        var result = await ReadingListService.AddBookAsync(Id, book.Id);
        if (result.IsSuccess)
        {
            searchInput = string.Empty;
            searchResults = [];
            showSearch = false;
            await LoadAsync();
        }
    }

    private async Task RemoveBookAsync(int bookId)
    {
        var result = await ReadingListService.RemoveBookAsync(Id, bookId);
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    /// <summary>
    /// Reorder by swapping the two adjacent positions. We send the *new* full ordering to the
    /// server so the back-end is the source of truth and a concurrent edit can't shear the list.
    /// </summary>
    private async Task MoveAsync(int fromIndex, int toIndex)
    {
        if (list is null || reordering)
        {
            return;
        }

        if (toIndex < 0 || toIndex >= list.Items.Count)
        {
            return;
        }

        reordering = true;
        try
        {
            var ids = list.Items.Select(i => i.Id).ToList();
            (ids[fromIndex], ids[toIndex]) = (ids[toIndex], ids[fromIndex]);

            var result = await ReadingListService.ReorderAsync(Id, ids);
            if (result.IsSuccess)
            {
                await LoadAsync();
            }
        }
        finally
        {
            reordering = false;
        }
    }

    private async Task<IReadOnlyList<BookListItemDto>> SearchBooksInReadingListForBannerAsync(string query)
    {
        var result = await BookService.SearchAsync(new BookSearchRequest
        {
            ReadingListId = Id,
            Query = string.IsNullOrWhiteSpace(query) ? null : query,
            Page = 1,
            PageSize = 20,
        });
        return result.IsSuccess ? result.Value.Items : [];
    }

    private async Task UploadReadingListBannerAsync(IBrowserFile file)
    {
        await using var s = file.OpenReadStream(2_000_000);
        var result = await ReadingListService.UploadCardBannerAsync(Id, s, file.Name, file.Size);
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private sealed class EditModel
    {
        [Required, StringLength(256)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
    }
}