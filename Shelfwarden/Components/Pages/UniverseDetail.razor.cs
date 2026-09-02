namespace Shelfwarden.Components.Pages;

public partial class UniverseDetail : ComponentBase
{
    private bool busy;
    private bool editing;
    private EditModel editModel = new();
    private bool editingTimeline;
    private string? errorMessage;
    private string? listErrorMessage;
    private bool loading = true;
    private CreateListModel newList = new();
    private readonly ListReorderState reorder = new();
    private bool reordering;
    private bool savingEdit;
    private CancellationTokenSource? searchCts;
    private string? searchInput;
    private IReadOnlyList<BookListItemDto> searchResults = [];
    private bool showSearch;
    private UniverseTab tab = UniverseTab.Overview;
    private UniverseDetailDto? universe;

    [Parameter]
    public int Id { get; set; }

    private IEnumerable<TabDescriptor> Tabs => universe is null
        ? []
        :
        [
            new(UniverseTab.Overview, "Overview", "bi-info-circle", 0),
            new(UniverseTab.Series, "Series", "bi-collection-fill", universe.Series.Count),
            new(UniverseTab.Books, "Books", "bi-book", universe.Timeline.Count),
            new(UniverseTab.ReadingLists, "Reading orders", "bi-list-ol", universe.ReadingLists.Count),
            new(UniverseTab.Timeline, "Timeline", "bi-clock-history", 0),
        ];

    protected override async Task OnParametersSetAsync()
    {
        loading = true;
        await LoadAsync();
        loading = false;
    }

    private async Task AddBookAsync(BookListItemDto book)
    {
        var result = await UniverseService.AddBooksAsync(Id, [book.Id]);
        if (result.IsSuccess)
        {
            searchInput = string.Empty;
            searchResults = [];
            showSearch = false;
            await LoadAsync();
        }
    }

    private async Task CreateReadingListAsync()
    {
        busy = true;
        listErrorMessage = null;
        try
        {
            var result = await UniverseService.CreateReadingListAsync(Id, new CreateReadingListRequest
            {
                Name = newList.Name,
                Description = newList.Description,
            });

            if (result.IsSuccess)
            {
                newList = new CreateListModel();
                await LoadAsync();
            }
            else
            {
                listErrorMessage = result.Errors.FirstOrDefault() ?? "Could not create the reading order.";
            }
        }
        finally
        {
            busy = false;
        }
    }

    private async Task DeleteAsync()
    {
        bool ok = await JS.InvokeAsync<bool>(
            "confirm",
            $"Delete universe \"{universe?.Name}\"? Its books and series stay in the library — only the "
            + "universe, its timeline and its reading orders are removed.");
        if (!ok)
        {
            return;
        }

        var result = await UniverseService.DeleteAsync(Id);
        if (result.IsSuccess)
        {
            SidebarNavRefresh.NotifyNavigationDataChanged();
            NavigationManager.NavigateTo("universes");
        }
    }

    private async Task LoadAsync()
    {
        var result = await UniverseService.GetByIdAsync(Id);
        if (result.IsSuccess)
        {
            universe = result.Value;
            editModel = new EditModel { Name = universe.Name, Description = universe.Description };
        }
        else
        {
            universe = null;
        }
    }

    /// <summary>
    /// Moves a timeline entry and sends the whole new ordering back, so the server stays the
    /// source of truth for <c>TimelineOrder</c>.
    /// </summary>
    private Task MoveAsync(int fromIndex, int toIndex) =>
        ApplyOrderAsync(universe is null
            ? null
            : ListReorderState.Move(universe.Timeline.Select(t => t.Id).ToList(), fromIndex, toIndex));

    private Task DropAsync() =>
        ApplyOrderAsync(universe is null
            ? null
            : reorder.Complete(universe.Timeline.Select(t => t.Id).ToList()));

    private async Task ApplyOrderAsync(List<int>? orderedIds)
    {
        if (orderedIds is null || reordering)
        {
            return;
        }

        reordering = true;
        try
        {
            var result = await UniverseService.ReorderTimelineAsync(Id, orderedIds);
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

            var existing = universe?.Timeline.Select(t => t.Book.Id).ToHashSet() ?? [];
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
        var result = await UniverseService.RemoveBookAsync(Id, bookId);
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    /// <summary>
    /// Detaches the series only. Its books keep their own universe membership, because the two
    /// relationships are deliberately independent.
    /// </summary>
    private async Task RemoveSeriesAsync(UniverseSeriesDto series)
    {
        bool ok = await JS.InvokeAsync<bool>(
            "confirm",
            $"Remove \"{series.Name}\" from this universe? Its books stay on the timeline unless you "
            + "remove them too.");
        if (!ok)
        {
            return;
        }

        var result = await UniverseService.SetSeriesUniverseAsync(series.Id, null);
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private async Task SaveAsync()
    {
        savingEdit = true;
        errorMessage = null;
        try
        {
            var result = await UniverseService.UpdateAsync(Id, new UpdateUniverseRequest
            {
                Name = editModel.Name,
                Description = editModel.Description,
            });

            if (result.IsSuccess)
            {
                editing = false;
                SidebarNavRefresh.NotifyNavigationDataChanged();
                await LoadAsync();
            }
            else
            {
                errorMessage = result.Errors.FirstOrDefault() ?? "Could not save the universe.";
            }
        }
        finally
        {
            savingEdit = false;
        }
    }

    private async Task SaveTimelineDateAsync(int bookId, string? timelineDate)
    {
        var result = await UniverseService.SetTimelineDateAsync(Id, bookId, timelineDate);
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private enum UniverseTab
    {
        Overview,
        Series,
        Books,
        ReadingLists,
        Timeline,
    }

    private sealed record TabDescriptor(UniverseTab Key, string Label, string Icon, int Count);

    private sealed class CreateListModel
    {
        public string? Description { get; set; }

        [Required, StringLength(256)]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class EditModel
    {
        public string? Description { get; set; }

        [Required, StringLength(256)]
        public string Name { get; set; } = string.Empty;
    }
}
