using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Pages;

public partial class UniverseDetail : ComponentBase
{
    private readonly ListReorderState bookReorder = new();
    private bool busy;
    private bool dateModalBusy;
    private string? dateModalError;
    private int? dateModalId;
    private bool dateModalOpen;
    private string? dateModalText;
    private TimelineType dateModalTimelineType;
    private int? dateModalYearFrom;
    private int? dateModalYearTo;
    private int? draggingGroupIndex;
    private bool editing;
    private EditModel editModel = new();
    private bool editingTimeline;
    private string? errorMessage;
    private string? listErrorMessage;
    private bool loading = true;
    private CreateListModel newList = new();
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
            new(UniverseTab.Books, "Books", "bi-book", universe.Timeline.Entries.Count),
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

    /// <summary>Index of a date in the universe's date list, or -1 for the unscheduled column.</summary>
    private int DateIndexOf(int? timelineDateId) =>
        timelineDateId is int id && universe is not null
            ? universe.Timeline.Dates.ToList().FindIndex(d => d.Id == id)
            : -1;

    private Task MoveDateAsync(int fromIndex, int toIndex) =>
        ApplyDateOrderAsync(universe is null
            ? null
            : ListReorderState.Move(universe.Timeline.Dates.Select(d => d.Id).ToList(), fromIndex, toIndex));

    private async Task ApplyDateOrderAsync(List<int>? orderedIds)
    {
        if (orderedIds is null || reordering)
        {
            return;
        }

        reordering = true;
        try
        {
            var result = await UniverseService.ReorderTimelineDatesAsync(Id, orderedIds);
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

    // Books are only ever dragged within their own date, so one drag state plus the group it
    // started in is enough to keep the highlighting from bleeding across the other groups.
    private void StartBookDrag(int groupIndex, int index)
    {
        draggingGroupIndex = groupIndex;
        bookReorder.Start(index);
    }

    private void BookDragOver(int groupIndex, int index)
    {
        if (draggingGroupIndex == groupIndex)
        {
            bookReorder.DragOver(index);
        }
    }

    private bool IsBookSource(int groupIndex, int index) =>
        draggingGroupIndex == groupIndex && bookReorder.IsSource(index);

    private bool IsBookDropTarget(int groupIndex, int index) =>
        draggingGroupIndex == groupIndex && bookReorder.IsDropTarget(index);

    private Task DropBookAsync(int groupIndex, int? timelineDateId)
    {
        if (universe is null || draggingGroupIndex != groupIndex)
        {
            bookReorder.Cancel();
            return Task.CompletedTask;
        }

        draggingGroupIndex = null;
        var ids = universe.Timeline.Groups[groupIndex].Entries.Select(e => e.Id).ToList();
        return ApplyGroupOrderAsync(timelineDateId, bookReorder.Complete(ids));
    }

    private Task MoveBookAsync(UniverseTimelineGroupDto group, int fromIndex, int toIndex) =>
        ApplyGroupOrderAsync(
            group.TimelineDateId,
            ListReorderState.Move(group.Entries.Select(e => e.Id).ToList(), fromIndex, toIndex));

    private async Task ApplyGroupOrderAsync(int? timelineDateId, List<int>? orderedIds)
    {
        if (orderedIds is null || reordering)
        {
            return;
        }

        reordering = true;
        try
        {
            var result = await UniverseService.ReorderTimelineGroupAsync(Id, timelineDateId, orderedIds);
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

    private async Task MoveBookToDateAsync(int bookId, string? rawTimelineDateId)
    {
        if (reordering)
        {
            return;
        }

        int? timelineDateId = int.TryParse(rawTimelineDateId, out int parsed) ? parsed : null;

        reordering = true;
        try
        {
            var result = await UniverseService.SetBookTimelineDateAsync(Id, bookId, timelineDateId);
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

    private void OpenDateModal(int? timelineDateId)
    {
        var existing = timelineDateId is int id
            ? universe?.Timeline.Dates.FirstOrDefault(d => d.Id == id)
            : null;

        dateModalId = timelineDateId;
        dateModalText = existing?.Name;
        dateModalYearFrom = existing?.YearFrom;
        dateModalYearTo = existing?.YearTo;
        dateModalTimelineType = existing is { YearFrom: not null } or { YearTo: not null }
            ? TimelineType.Numeric
            : universe?.TimelineType ?? TimelineType.Named;
        dateModalError = null;
        dateModalOpen = true;
    }

    private void CloseDateModal()
    {
        dateModalOpen = false;
        dateModalBusy = false;
        dateModalError = null;
    }

    private async Task DateModalKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SaveDateAsync();
        }
        else if (e.Key == "Escape")
        {
            CloseDateModal();
        }
    }

    private bool DateModalCanSave =>
        dateModalTimelineType == TimelineType.Named
            ? !string.IsNullOrWhiteSpace(dateModalText)
            : dateModalYearFrom.HasValue || dateModalYearTo.HasValue;

    private async Task SaveDateAsync()
    {
        if (dateModalBusy || !DateModalCanSave)
        {
            return;
        }

        if (dateModalYearFrom is int yf && dateModalYearTo is int yt && yf > yt)
        {
            dateModalError = "Year from must not be after year to.";
            return;
        }

        // Only send the fields the selected radio actually shows — the other pair stays null so
        // the service can tell which mode the admin picked.
        string? date = dateModalTimelineType == TimelineType.Named ? dateModalText : null;
        int? yearFrom = dateModalTimelineType == TimelineType.Numeric ? dateModalYearFrom : null;
        int? yearTo = dateModalTimelineType == TimelineType.Numeric ? dateModalYearTo : null;

        dateModalBusy = true;
        dateModalError = null;
        try
        {
            var result = dateModalId is int id
                ? (await UniverseService.RenameTimelineDateAsync(id, date, yearFrom, yearTo)).Map(_ => 0)
                : (await UniverseService.CreateTimelineDateAsync(Id, date, yearFrom, yearTo)).Map(_ => 0);

            if (result.IsSuccess)
            {
                dateModalOpen = false;
                await LoadAsync();
            }
            else
            {
                dateModalError = result.Errors.FirstOrDefault()
                    ?? result.ValidationErrors.FirstOrDefault()?.ErrorMessage
                    ?? "Could not save the date.";
            }
        }
        finally
        {
            dateModalBusy = false;
        }
    }

    private async Task DeleteDateAsync(int timelineDateId, string label, int bookCount)
    {
        string question = bookCount == 0
            ? $"Delete the timeline date \"{label}\"?"
            : $"Delete the timeline date \"{label}\"? Its {bookCount} book{(bookCount == 1 ? "" : "s")} "
              + "stay in the universe and go back to being unscheduled.";

        if (!await JS.InvokeAsync<bool>("confirm", question))
        {
            return;
        }

        var result = await UniverseService.DeleteTimelineDateAsync(timelineDateId);
        if (result.IsSuccess)
        {
            await LoadAsync();
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

            var existing = universe?.Timeline.Entries.Select(t => t.Book.Id).ToHashSet() ?? [];
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
