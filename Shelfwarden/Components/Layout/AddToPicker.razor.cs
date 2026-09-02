namespace Shelfwarden.Components.Layout;

/// <summary>What a book or series is being added to.</summary>
public enum AddToTarget
{
    Collection,
    ReadingList,
    Universe,
}

/// <summary>
/// Shared "Add to&hellip;" modal, hosted once in <see cref="MainLayout"/> and opened through a
/// <see cref="CascadingParameter"/> from <see cref="Shared.AddToActions"/>. Listing every
/// collection / list / universe inline in a context menu doesn't scale past a handful, so all
/// three targets funnel through this one dialog.
/// </summary>
public partial class AddToPicker : ComponentBase
{
    private string? actionError;
    private bool addSeriesBooksToTimeline = true;
    private bool busy;
    private bool creatingNew;
    private bool loading;
    private string? loadError;
    private string newName = string.Empty;
    private List<Option> options = [];
    private int selectedId;

    public int? BookId { get; private set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    public bool IsOpen { get; private set; }

    public int? SeriesId { get; private set; }

    public AddToTarget Target { get; private set; }

    [Inject]
    private ICollectionService CollectionService { get; set; } = null!;

    /// <summary>Name of the thing being added, shown so the user knows what they picked.</summary>
    private string? SubjectName { get; set; }

    [Inject]
    private IReadingListService ReadingListService { get; set; } = null!;

    [Inject]
    private IUniverseService UniverseService { get; set; } = null!;

    [Inject]
    private IUserContextService UserContext { get; set; } = null!;

    /// <summary>Universes are catalog data, so only administrators may add one on the fly.</summary>
    private bool CanCreate => Target != AddToTarget.Universe || UserContext.IsAdministrator();

    private bool CanConfirm => creatingNew
        ? !string.IsNullOrWhiteSpace(newName)
        : options.Count > 0 && selectedId > 0;

    private string EmptyMessage => Target switch
    {
        AddToTarget.Collection => "You don't have any collections yet.",
        AddToTarget.ReadingList => "You don't have any reading lists yet.",
        _ => "No universes have been created yet.",
    };

    private string SubjectSummary => (Target, SeriesId) switch
    {
        // A series belongs to at most one universe, so this is a move rather than an addition.
        (AddToTarget.Universe, not null) => $"\"{SubjectName}\" will be set in the chosen universe, replacing any universe it's in now.",
        (_, not null) => $"Every book in \"{SubjectName}\" will be added, in series order. Books already there are skipped.",
        (AddToTarget.Universe, _) => $"\"{SubjectName}\" will be added to the end of the universe's timeline.",
        _ => $"\"{SubjectName}\" will be added to the end.",
    };

    private string TargetIcon => Target switch
    {
        AddToTarget.Collection => "bi-bookshelf",
        AddToTarget.ReadingList => "bi-list-ol",
        _ => "bi-stars",
    };

    private string TargetNoun => Target switch
    {
        AddToTarget.Collection => "Collection",
        AddToTarget.ReadingList => "Reading list",
        _ => "Universe",
    };

    private string TargetTitle => $"Add to {TargetNoun.ToLowerInvariant()}";

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        BookId = null;
        SeriesId = null;
        StateHasChanged();
    }

    public void OpenForBook(AddToTarget target, int bookId, string? bookTitle)
    {
        BookId = bookId;
        SeriesId = null;
        SubjectName = bookTitle;
        BeginOpen(target);
    }

    public void OpenForSeries(AddToTarget target, int seriesId, string? seriesName)
    {
        SeriesId = seriesId;
        BookId = null;
        SubjectName = seriesName;
        BeginOpen(target);
    }

    private void BeginOpen(AddToTarget target)
    {
        Target = target;
        IsOpen = true;
        loadError = null;
        actionError = null;
        creatingNew = false;
        newName = string.Empty;
        addSeriesBooksToTimeline = true;
        options = [];
        selectedId = 0;
        _ = LoadOptionsAsync();
        StateHasChanged();
    }

    private void StartCreate()
    {
        creatingNew = true;
        actionError = null;
    }

    private async Task LoadOptionsAsync()
    {
        loading = true;
        try
        {
            options = Target switch
            {
                // Global collections are catalog data, so they're only offered to administrators —
                // anyone else would just get a Forbidden back when they tried to add.
                AddToTarget.Collection => (await CollectionService.ListAsync()) is { IsSuccess: true } c
                    ? c.Value
                        .Where(x => !x.IsGlobal || UserContext.IsAdministrator())
                        .Select(x => new Option(x.Id, x.Name))
                        .ToList()
                    : Fail("Could not load collections."),
                AddToTarget.ReadingList => (await ReadingListService.ListAsync()) is { IsSuccess: true } l
                    ? l.Value.Select(x => new Option(x.Id, x.Name)).ToList()
                    : Fail("Could not load reading lists."),
                _ => (await UniverseService.SearchAsync(limit: 500)) is { IsSuccess: true } u
                    ? u.Value.Select(x => new Option(x.Id, x.Name)).ToList()
                    : Fail("Could not load universes."),
            };

            selectedId = options.FirstOrDefault()?.Id ?? 0;

            // Nothing to pick from, so go straight to the create form when that's allowed.
            if (options.Count == 0 && loadError is null && CanCreate)
            {
                creatingNew = true;
            }
        }
        finally
        {
            loading = false;
            await InvokeAsync(StateHasChanged);
        }

        List<Option> Fail(string message)
        {
            loadError = message;
            return [];
        }
    }

    private async Task ConfirmAsync()
    {
        actionError = null;
        busy = true;
        try
        {
            int targetId = selectedId;
            if (creatingNew)
            {
                int? created = await CreateTargetAsync();
                if (created is not int newId)
                {
                    return;
                }

                targetId = newId;
            }

            var (success, error) = await AddAsync(targetId);
            if (success)
            {
                Close();
            }
            else
            {
                actionError = error ?? $"Could not add to this {TargetNoun.ToLowerInvariant()}.";
            }
        }
        finally
        {
            busy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task<int?> CreateTargetAsync()
    {
        string name = newName.Trim();
        switch (Target)
        {
            case AddToTarget.Collection:
                var collection = await CollectionService.CreateAsync(new CreateCollectionRequest { Name = name });
                if (!collection.IsSuccess)
                {
                    actionError = collection.Errors.FirstOrDefault() ?? "Could not create the collection.";
                    return null;
                }

                return collection.Value.Id;

            case AddToTarget.ReadingList:
                var list = await ReadingListService.CreateAsync(new CreateReadingListRequest { Name = name });
                if (!list.IsSuccess)
                {
                    actionError = list.Errors.FirstOrDefault() ?? "Could not create the reading list.";
                    return null;
                }

                return list.Value.Id;

            default:
                var universe = await UniverseService.CreateAsync(new CreateUniverseRequest { Name = name });
                if (!universe.IsSuccess)
                {
                    actionError = universe.Errors.FirstOrDefault() ?? "Could not create the universe.";
                    return null;
                }

                return universe.Value.Id;
        }
    }

    private async Task<(bool Success, string? Error)> AddAsync(int targetId)
    {
        if (SeriesId is int seriesId)
        {
            return Target switch
            {
                AddToTarget.Collection => Outcome(await CollectionService.AddSeriesAsync(targetId, seriesId)),
                AddToTarget.ReadingList => Outcome(await ReadingListService.AddSeriesAsync(targetId, seriesId)),
                // A series belongs to at most one universe, so this assigns rather than appends.
                _ => Outcome(await UniverseService.SetSeriesUniverseAsync(seriesId, targetId, addSeriesBooksToTimeline)),
            };
        }

        int bookId = BookId ?? 0;
        return Target switch
        {
            AddToTarget.Collection => Outcome(await CollectionService.AddBookAsync(targetId, bookId)),
            AddToTarget.ReadingList => Outcome(await ReadingListService.AddBookAsync(targetId, bookId)),
            _ => Outcome(await UniverseService.AddBooksAsync(targetId, [bookId])),
        };

        static (bool, string?) Outcome<T>(Result<T> result) =>
            (result.IsSuccess, result.Errors.FirstOrDefault());
    }

    private sealed record Option(int Id, string Name);
}
