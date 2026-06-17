namespace Shelfwarden.Components.Pages;

public partial class AuthorDetail : ComponentBase
{
    [Parameter] public int Id { get; set; }

    private AuthorDetailDto? author;
    private bool loadFailed;
    private IReadOnlyList<AdditionalContentItemDto>? extraContent;

    private bool selectMode;
    private readonly HashSet<int> selectedBookIds = [];
    private bool isAuthorEditModalOpen;
    private readonly Dictionary<int, long> authorPhotoVersions = [];

    private bool isAssignContentModalOpen;
    private string assignEntityLabel = "entity";
    private string? assignEntityName;
    private string assignEntityType = "book";
    private int assignEntityId;

    private string pseudonymQuery = string.Empty;
    private List<AuthorDto> pseudonymSuggestions = [];
    private bool showPseudonymSuggestions;
    private string? pseudonymError;

    protected override async Task OnParametersSetAsync()
    {
        author = null;
        loadFailed = false;
        extraContent = null;
        selectMode = false;
        selectedBookIds.Clear();
        isAuthorEditModalOpen = false;
        isAssignContentModalOpen = false;
        pseudonymQuery = string.Empty;
        pseudonymSuggestions = [];
        showPseudonymSuggestions = false;
        pseudonymError = null;

        var detailTask = AuthorService.GetDetailAsync(Id);
        var contentTask = ContentService.GetForAuthorAsync(Id);
        await Task.WhenAll(detailTask, contentTask);

        if (detailTask.Result.IsSuccess)
        {
            author = detailTask.Result.Value;
        }
        else
        {
            loadFailed = true;
        }

        if (contentTask.Result.IsSuccess)
        {
            extraContent = contentTask.Result.Value;
        }
    }

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

    private void SelectAll()
    {
        if (author is null)
        {
            return;
        }

        foreach (var b in author.StandaloneBooks)
        {
            selectedBookIds.Add(b.Id);
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
        NavigationManager.NavigateTo($"books/batch-edit?ids={ids}&return=authors/{Id}");
    }

    private void OpenAssignContentSeries(int seriesId, string seriesName)
    {
        assignEntityId = seriesId;
        assignEntityType = "series";
        assignEntityLabel = "series";
        assignEntityName = seriesName;
        isAssignContentModalOpen = true;
    }

    private void OpenAssignContentBook(int bookId, string bookTitle)
    {
        assignEntityId = bookId;
        assignEntityType = "book";
        assignEntityLabel = "book";
        assignEntityName = bookTitle;
        isAssignContentModalOpen = true;
    }

    private void CloseAssignContent() => isAssignContentModalOpen = false;

    private async Task RefreshExtraContentAsync()
    {
        var result = await ContentService.GetForAuthorAsync(Id);
        if (result.IsSuccess)
        {
            extraContent = result.Value;
        }
    }

    private void OnContentItemRenamed(AdditionalContentItemDto renamed)
    {
        if (extraContent is null) return;
        extraContent = extraContent
            .Select(i => i.Id == renamed.Id ? renamed : i)
            .ToList();
    }

    private void OpenAuthorEditModal() => isAuthorEditModalOpen = true;

    private Task CloseAuthorEditModalAsync()
    {
        isAuthorEditModalOpen = false;
        return Task.CompletedTask;
    }

    private async Task OnAuthorSavedAsync()
    {
        authorPhotoVersions[Id] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = await AuthorService.GetDetailAsync(Id);
        if (result.IsSuccess && author is not null)
        {
            author = result.Value;
        }
    }

    private string GetAuthorPhotoUrl(int authorId)
    {
        long version = authorPhotoVersions.GetValueOrDefault(authorId, 0);
        return $"author-photos/{authorId}?v={version}";
    }

    private async Task OnPseudonymInput(ChangeEventArgs e)
    {
        pseudonymQuery = e.Value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pseudonymQuery))
        {
            pseudonymSuggestions = [];
            showPseudonymSuggestions = false;
            return;
        }

        var result = await AuthorService.SearchAsync(pseudonymQuery, limit: 10);

        // Exclude the author itself, any already-linked pseudonyms, and this author's primary.
        var excluded = new HashSet<int>(author?.Pseudonyms.Select(p => p.Id) ?? []) { Id };
        if (author?.PrimaryAuthor is { } primary)
        {
            excluded.Add(primary.Id);
        }

        pseudonymSuggestions = result.IsSuccess
            ? result.Value.Where(a => !excluded.Contains(a.Id)).ToList()
            : [];
        showPseudonymSuggestions = pseudonymSuggestions.Count > 0;
    }

    private async Task AddPseudonymAsync(int pseudonymId)
    {
        pseudonymError = null;
        var result = await AuthorService.LinkPseudonymAsync(Id, pseudonymId);
        if (!result.IsSuccess)
        {
            pseudonymError = result.Errors.FirstOrDefault()
                ?? result.ValidationErrors.Select(v => v.ErrorMessage).FirstOrDefault()
                ?? "Could not link the pseudonym.";
            return;
        }

        pseudonymQuery = string.Empty;
        pseudonymSuggestions = [];
        showPseudonymSuggestions = false;
        await ReloadDetailAsync();
    }

    private async Task UnlinkPseudonymAsync(int pseudonymId)
    {
        pseudonymError = null;
        var result = await AuthorService.UnlinkPseudonymAsync(pseudonymId);
        if (!result.IsSuccess)
        {
            pseudonymError = result.Errors.FirstOrDefault() ?? "Could not unlink the pseudonym.";
            return;
        }

        await ReloadDetailAsync();
    }

    private async Task UnlinkSelfAsync()
    {
        pseudonymError = null;
        var result = await AuthorService.UnlinkPseudonymAsync(Id);
        if (!result.IsSuccess)
        {
            pseudonymError = result.Errors.FirstOrDefault() ?? "Could not unlink from the primary author.";
            return;
        }

        await ReloadDetailAsync();
    }

    private async Task ReloadDetailAsync()
    {
        var result = await AuthorService.GetDetailAsync(Id);
        if (result.IsSuccess)
        {
            author = result.Value;
        }
    }
}