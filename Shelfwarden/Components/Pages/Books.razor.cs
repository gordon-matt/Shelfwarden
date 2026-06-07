namespace Shelfwarden.Components.Pages;

public partial class Books : ComponentBase
{
    [SupplyParameterFromQuery(Name = "q")]
    public string? QueryFromUrl { get; set; }

    [SupplyParameterFromQuery(Name = "view")]
    public string? ViewFromUrl { get; set; }

    [SupplyParameterFromQuery(Name = "series")]
    public int? SeriesFromUrl { get; set; }

    /// <summary>Legacy alias for <c>?series=</c>; book detail used this name briefly.</summary>
    [SupplyParameterFromQuery(Name = "seriesId")]
    public int? SeriesIdFromUrl { get; set; }

    [SupplyParameterFromQuery(Name = "author")]
    public int? AuthorFromUrl { get; set; }

    [SupplyParameterFromQuery(Name = "genreId")]
    public int? GenreFromUrl { get; set; }

    [SupplyParameterFromQuery(Name = "genre")]
    public int? GenreAliasFromUrl { get; set; }

    [SupplyParameterFromQuery(Name = "tagId")]
    public int? TagFromUrl { get; set; }

    private enum ViewMode
    { Grid, List }

    private enum TagFilterMode
    { Any, None, Selected }

    private readonly List<BookListItemDto> results = [];
    private readonly string observerKey = $"books-page-{Guid.NewGuid():N}";
    private string? query;
    private string shelfId = "";
    private string collectionId = "";
    private string authorId = "";
    private string seriesId = "";
    private string genreId = "";
    private TagFilterMode tagFilterMode = TagFilterMode.Any;
    private readonly List<TagDto> selectedTagFilters = [];
    private bool awaitingReview;
    private BookReadStatusFilter readStatus = BookReadStatusFilter.Any;
    private BookSortBy sortBy = BookSortBy.Title;
    private bool sortDescending;
    private int nextPageToLoad = 1;
    private ViewMode viewMode = ViewMode.Grid;
    private int pageSize = 24;
    private int totalCount;
    private bool hasMorePages;
    private bool isLoadingMore;
    private string? startsWithFilter;
    private ElementReference infiniteScrollSentinel;
    private DotNetObjectReference<Books>? dotNetRef;
    private CancellationTokenSource? searchDebounceCts;

    private bool selectMode;
    private bool selectAllMatchingBusy;
    private bool showAdvancedFilters;
    private readonly HashSet<int> selectedBookIds = [];

    private IReadOnlyList<ShelfDto> shelves = [];
    private IReadOnlyList<CollectionDto> collections = [];
    private IReadOnlyList<AuthorDto> authors = [];
    private IReadOnlyList<SeriesDto> series = [];
    private IReadOnlyList<GenreDto> genres = [];
    private IReadOnlyList<TagDto> tags = [];

    private bool HasActiveAdvancedFilters =>
        !string.IsNullOrEmpty(shelfId)
        || !string.IsNullOrEmpty(collectionId)
        || !string.IsNullOrEmpty(authorId)
        || !string.IsNullOrEmpty(seriesId)
        || !string.IsNullOrEmpty(genreId)
        || tagFilterMode == TagFilterMode.None
        || (tagFilterMode == TagFilterMode.Selected && selectedTagFilters.Count > 0)
        || awaitingReview
        || readStatus != BookReadStatusFilter.Any;

    private bool HasActiveFilters =>
        HasActiveAdvancedFilters
        || !string.IsNullOrWhiteSpace(startsWithFilter)
        || !string.IsNullOrWhiteSpace(query);

    private bool UseLetterRail => string.IsNullOrEmpty(seriesId);
    private int BulkEditMaxBookCount => Math.Max(1, Configuration.GetValue<int?>("BulkEditMaxBookCount") ?? 100);
    private bool CanSelectAllMatching => totalCount > 0 && totalCount <= BulkEditMaxBookCount;

    protected override async Task OnInitializedAsync()
    {
        // Load filter options + first page in parallel — they're independent queries.
        var shelvesTask = ShelfService.GetAllAsync();
        var collectionsTask = CollectionService.ListAsync();
        var authorsTask = AuthorService.SearchAsync(null, limit: 500);
        var seriesTask = SeriesService.SearchAsync(null, limit: 500);
        var genresTask = GenreService.ListAsync();
        var tagsTask = TagService.ListAsync();

        await Task.WhenAll(shelvesTask, collectionsTask, authorsTask, seriesTask, genresTask, tagsTask);

        if (shelvesTask.Result.IsSuccess)
        {
            shelves = shelvesTask.Result.Value;
        }

        if (collectionsTask.Result.IsSuccess)
        {
            collections = collectionsTask.Result.Value;
        }

        if (authorsTask.Result.IsSuccess)
        {
            authors = authorsTask.Result.Value;
        }

        if (seriesTask.Result.IsSuccess)
        {
            series = seriesTask.Result.Value;
        }

        if (genresTask.Result.IsSuccess)
        {
            genres = genresTask.Result.Value;
        }

        if (tagsTask.Result.IsSuccess)
        {
            tags = tagsTask.Result.Value;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        // The topbar search posts its term as ?q=… — pick that up so search drops the user
        // straight onto a filtered books grid.
        if (!string.IsNullOrWhiteSpace(QueryFromUrl) && QueryFromUrl != query)
        {
            query = QueryFromUrl;
        }
        if (string.Equals(ViewFromUrl, "list", StringComparison.OrdinalIgnoreCase))
        {
            viewMode = ViewMode.List;
            pageSize = 12;
        }
        // ?series= (and legacy ?seriesId=) plus ?author= make the books grid composable from other pages.
        int? seriesFromQuery = SeriesFromUrl ?? SeriesIdFromUrl;
        if (seriesFromQuery is { } sId && seriesId != sId.ToString())
        {
            seriesId = sId.ToString();
        }
        if (AuthorFromUrl is { } aId && authorId != aId.ToString())
        {
            authorId = aId.ToString();
        }
        int? incomingGenreId = GenreFromUrl ?? GenreAliasFromUrl;
        if (incomingGenreId is { } gId && genreId != gId.ToString())
        {
            genreId = gId.ToString();
        }
        if (TagFromUrl is { } tid && tid > 0)
        {
            tagFilterMode = TagFilterMode.Selected;
            if (!selectedTagFilters.Any(t => t.Id == tid))
            {
                var resolved = tags.FirstOrDefault(t => t.Id == tid);
                if (resolved is not null)
                {
                    selectedTagFilters.Add(resolved);
                }
            }
        }

        if (HasActiveAdvancedFilters)
        {
            showAdvancedFilters = true;
        }

        dotNetRef ??= DotNetObjectReference.Create(this);
        await ResetAndLoadAsync();
    }

    private async Task SetViewMode(ViewMode mode)
    {
        if (viewMode == mode)
        {
            return;
        }

        viewMode = mode;
        // List rows are larger so render fewer per page; grid uses the dense default.
        pageSize = mode == ViewMode.List ? 12 : 24;
        await ResetAndLoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!UseLetterRail)
        {
            return;
        }

        if (!hasMorePages && !isLoadingMore)
        {
            return;
        }

        await JSRuntime.InvokeVoidAsync(
            "shelfwarden.observeInfiniteScroll",
            observerKey,
            infiniteScrollSentinel,
            dotNetRef,
            nameof(LoadMoreAsync));
    }

    private async Task ResetAndLoadAsync()
    {
        results.Clear();
        nextPageToLoad = 1;
        totalCount = 0;
        hasMorePages = false;
        if (!UseLetterRail)
        {
            await LoadAllSeriesBooksAsync();
        }
        else
        {
            await LoadMoreAsync();
        }
    }

    [JSInvokable]
    public async Task LoadMoreAsync()
    {
        if (isLoadingMore || (!hasMorePages && nextPageToLoad > 1))
        {
            return;
        }

        isLoadingMore = true;

        var result = await BookService.SearchAsync(new BookSearchRequest
        {
            Page = nextPageToLoad,
            PageSize = pageSize,
            Query = string.IsNullOrWhiteSpace(query) ? null : query,
            StartsWith = startsWithFilter,
            ShelfId = ParseId(shelfId),
            AuthorId = ParseFilterId(authorId),
            SeriesId = ParseFilterId(seriesId),
            GenreId = ParseFilterId(genreId),
            CollectionId = ParseFilterId(collectionId),
            TagId = GetTagFilterId(),
            TagIds = GetSelectedTagFilterIds(),
            AwaitingReview = awaitingReview,
            ReadStatus = readStatus,
            SortBy = sortBy,
            SortDescending = sortDescending,
        });

        if (result.IsSuccess)
        {
            var page = result.Value;
            results.AddRange(page.Items);
            totalCount = page.TotalCount;
            hasMorePages = nextPageToLoad < page.TotalPages;
            nextPageToLoad++;
        }
        else
        {
            hasMorePages = false;
        }

        isLoadingMore = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadAllSeriesBooksAsync()
    {
        isLoadingMore = true;
        const int seriesPageSize = 200;
        int page = 1;

        while (true)
        {
            var result = await BookService.SearchAsync(new BookSearchRequest
            {
                Page = page,
                PageSize = seriesPageSize,
                Query = string.IsNullOrWhiteSpace(query) ? null : query,
                ShelfId = ParseId(shelfId),
                AuthorId = ParseFilterId(authorId),
                SeriesId = ParseFilterId(seriesId),
                GenreId = ParseFilterId(genreId),
                CollectionId = ParseFilterId(collectionId),
                TagId = GetTagFilterId(),
                TagIds = GetSelectedTagFilterIds(),
                AwaitingReview = awaitingReview,
                ReadStatus = readStatus,
                SortBy = sortBy,
                SortDescending = sortDescending,
            });

            if (!result.IsSuccess)
            {
                hasMorePages = false;
                break;
            }

            var chunk = result.Value;
            results.AddRange(chunk.Items);
            totalCount = chunk.TotalCount;

            if (page >= chunk.TotalPages)
            {
                break;
            }

            page++;
        }

        hasMorePages = false;
        nextPageToLoad = page + 1;
        isLoadingMore = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnFilterChangedAsync() => await ResetAndLoadAsync();

    private async Task OnTagModeChangedAsync()
    {
        if (tagFilterMode != TagFilterMode.Selected)
        {
            selectedTagFilters.Clear();
        }

        await ResetAndLoadAsync();
    }

    private async Task OnSearchQueryChangedAsync()
    {
        searchDebounceCts?.Cancel();
        searchDebounceCts?.Dispose();
        searchDebounceCts = new CancellationTokenSource();
        var token = searchDebounceCts.Token;

        try
        {
            await Task.Delay(350, token);
            await ResetAndLoadAsync();
        }
        catch (TaskCanceledException)
        {
            // Ignore canceled debounce timers when user keeps typing.
        }
    }

    private async Task ClearFiltersAsync()
    {
        query = null;
        shelfId = "";
        collectionId = "";
        authorId = "";
        seriesId = "";
        genreId = "";
        tagFilterMode = TagFilterMode.Any;
        selectedTagFilters.Clear();
        awaitingReview = false;
        readStatus = BookReadStatusFilter.Any;
        startsWithFilter = null;
        await ResetAndLoadAsync();
    }

    private void ToggleAdvancedFilters() => showAdvancedFilters = !showAdvancedFilters;

    private void ToggleSelectMode()
    {
        selectMode = !selectMode;
        if (!selectMode)
        {
            selectedBookIds.Clear();
        }
    }

    private void ToggleSelection(int id, bool include)
    {
        if (include)
        {
            selectedBookIds.Add(id);
        }
        else
        {
            selectedBookIds.Remove(id);
        }
    }

    private void SelectAllVisible()
    {
        foreach (var b in results)
        {
            selectedBookIds.Add(b.Id);
        }
    }

    private async Task SelectAllMatchingAsync()
    {
        if (selectAllMatchingBusy || !CanSelectAllMatching)
        {
            return;
        }

        selectAllMatchingBusy = true;
        try
        {
            const int batch = 200;
            int page = 1;
            while (true)
            {
                var result = await BookService.SearchAsync(new BookSearchRequest
                {
                    Page = page,
                    PageSize = batch,
                    Query = string.IsNullOrWhiteSpace(query) ? null : query,
                    StartsWith = startsWithFilter,
                    ShelfId = ParseId(shelfId),
                    AuthorId = ParseFilterId(authorId),
                    SeriesId = ParseFilterId(seriesId),
                    GenreId = ParseFilterId(genreId),
                    CollectionId = ParseFilterId(collectionId),
                    TagId = GetTagFilterId(),
                    TagIds = GetSelectedTagFilterIds(),
                    AwaitingReview = awaitingReview,
                    ReadStatus = readStatus,
                    SortBy = sortBy,
                    SortDescending = sortDescending,
                });

                if (!result.IsSuccess)
                {
                    break;
                }

                var chunk = result.Value;
                foreach (var b in chunk.Items)
                {
                    selectedBookIds.Add(b.Id);
                }

                if (page >= chunk.TotalPages)
                {
                    break;
                }

                page++;
            }
        }
        finally
        {
            selectAllMatchingBusy = false;
        }
    }

    private void ClearSelection() => selectedBookIds.Clear();

    private void GoToBatchEdit()
    {
        if (selectedBookIds.Count == 0)
        {
            return;
        }

        string ids = string.Join(',', selectedBookIds);
        // Round-trip back to the books page (preserving the current URL ish — simplest is /books).
        NavigationManager.NavigateTo($"books/batch-edit?ids={ids}&return=books");
    }

    private static int? ParseId(string raw) =>
        int.TryParse(raw, out int id) && id > 0 ? id : null;

    private static int? ParseFilterId(string raw) =>
        string.IsNullOrEmpty(raw) ? null : int.TryParse(raw, out int id) ? id : null;

    private int? GetTagFilterId() => tagFilterMode switch
    {
        TagFilterMode.None => -1,
        _ => null,
    };

    private IReadOnlyList<int> GetSelectedTagFilterIds()
        => tagFilterMode == TagFilterMode.Selected
            ? selectedTagFilters.Select(t => t.Id).Distinct().ToList()
            : [];

    private Task<IReadOnlyList<TagDto>> SearchTagFilterOptionsAsync(string queryText)
    {
        IEnumerable<TagDto> q = tags;
        if (!string.IsNullOrWhiteSpace(queryText))
        {
            string needle = queryText.Trim();
            q = q.Where(t => t.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var list = q
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return Task.FromResult<IReadOnlyList<TagDto>>(list);
    }

    private Task AddTagFilterAsync(string tagName)
    {
        string trimmed = tagName.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Task.CompletedTask;
        }

        var existing = tags.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !selectedTagFilters.Any(t => t.Id == existing.Id))
        {
            selectedTagFilters.Add(existing);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        searchDebounceCts?.Cancel();
        searchDebounceCts?.Dispose();
        dotNetRef?.Dispose();

        try
        {
            await JSRuntime.InvokeVoidAsync("shelfwarden.disconnectInfiniteScroll", observerKey);
        }
        catch
        {
            // Ignore disposal-time JS failures during teardown.
        }
    }
}