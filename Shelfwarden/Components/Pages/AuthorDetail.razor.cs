namespace Shelfwarden.Components.Pages;

public partial class AuthorDetail : ComponentBase
{
    private readonly Dictionary<int, long> authorPhotoVersions = [];
    private readonly HashSet<int> selectedBookIds = [];
    private int assignEntityId;
    private string assignEntityLabel = "entity";
    private string? assignEntityName;
    private string assignEntityType = "book";
    private AuthorDetailDto? author;
    private IReadOnlyList<AdditionalContentItemDto>? extraContent;
    private bool isAssignContentModalOpen;
    private bool isAuthorEditModalOpen;
    private bool isAuthorLinkModalOpen;
    private bool isPseudonymModalOpen;
    private string? linkError;
    private string linkName = string.Empty;
    private string linkUrl = string.Empty;
    private bool loadFailed;
    private string? pseudonymError;
    private string pseudonymQuery = string.Empty;
    private List<AuthorDto> pseudonymSuggestions = [];
    private bool selectMode;
    private bool showPseudonymSuggestions;
    [Parameter] public int Id { get; set; }
    private bool isAdministrator => UserContext.IsAdministrator();

    private bool ShowLinksRow =>
        author is { Id: > 0 } current
        && (current.Links.Count > 0 || isAdministrator);

    private bool ShowPseudonymsPanel =>
            author is { PrimaryAuthor: null } current
        && (current.Pseudonyms.Count > 0 || isAdministrator);

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
        isPseudonymModalOpen = false;
        linkName = string.Empty;
        linkUrl = string.Empty;
        linkError = null;
        isAuthorLinkModalOpen = false;

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

    private static string GetLinkDisplayName(AuthorLinkDto link)
    {
        if (!string.IsNullOrWhiteSpace(link.Name))
        {
            return link.Name;
        }

        return Uri.TryCreate(link.Url, UriKind.Absolute, out Uri? uri)
            ? uri.Host
            : link.Url;
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
        isPseudonymModalOpen = false;
        await ReloadDetailAsync();
    }

    private void ClearSelection() => selectedBookIds.Clear();

    private void CloseAssignContent() => isAssignContentModalOpen = false;

    private Task CloseAuthorEditModalAsync()
    {
        isAuthorEditModalOpen = false;
        return Task.CompletedTask;
    }

    private void CloseAuthorLinkModal()
    {
        isAuthorLinkModalOpen = false;
        linkName = string.Empty;
        linkUrl = string.Empty;
    }

    private void ClosePseudonymModal()
    {
        isPseudonymModalOpen = false;
        pseudonymQuery = string.Empty;
        pseudonymSuggestions = [];
        showPseudonymSuggestions = false;
    }

    private string GetAuthorPhotoUrl(int authorId)
    {
        long version = authorPhotoVersions.GetValueOrDefault(authorId, 0);
        return $"author-photos/{authorId}?v={version}";
    }

    private void GoToBatchEdit()
    {
        if (selectedBookIds.Count == 0)
        {
            return;
        }

        string ids = string.Join(',', selectedBookIds);
        NavigationManager.NavigateTo($"books/batch-edit?ids={ids}&return=authors/{Id}");
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

    private void OnContentItemRenamed(AdditionalContentItemDto renamed)
    {
        if (extraContent is null) return;
        extraContent = extraContent
            .Select(i => i.Id == renamed.Id ? renamed : i)
            .ToList();
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

    private void OpenAssignContentBook(int bookId, string bookTitle)
    {
        assignEntityId = bookId;
        assignEntityType = "book";
        assignEntityLabel = "book";
        assignEntityName = bookTitle;
        isAssignContentModalOpen = true;
    }

    private void OpenAssignContentSeries(int seriesId, string seriesName)
    {
        assignEntityId = seriesId;
        assignEntityType = "series";
        assignEntityLabel = "series";
        assignEntityName = seriesName;
        isAssignContentModalOpen = true;
    }

    private void OpenAuthorEditModal() => isAuthorEditModalOpen = true;

    private void OpenAuthorLinkModal()
    {
        linkError = null;
        linkName = string.Empty;
        linkUrl = string.Empty;
        isAuthorLinkModalOpen = true;
    }

    private void OpenPseudonymModal()
    {
        pseudonymError = null;
        pseudonymQuery = string.Empty;
        pseudonymSuggestions = [];
        showPseudonymSuggestions = false;
        isPseudonymModalOpen = true;
    }

    private async Task RefreshExtraContentAsync()
    {
        var result = await ContentService.GetForAuthorAsync(Id);
        if (result.IsSuccess)
        {
            extraContent = result.Value;
        }
    }

    private async Task ReloadDetailAsync()
    {
        var result = await AuthorService.GetDetailAsync(Id);
        if (result.IsSuccess)
        {
            author = result.Value;
        }
    }

    private async Task RemoveAuthorLinkAsync(int linkId)
    {
        linkError = null;
        var result = await AuthorService.RemoveAuthorLinkAsync(Id, linkId);
        if (!result.IsSuccess)
        {
            linkError = result.Errors.FirstOrDefault() ?? "Could not remove the link.";
            return;
        }

        await ReloadDetailAsync();
    }

    private async Task SaveAuthorLinkAsync()
    {
        linkError = null;
        var result = await AuthorService.AddAuthorLinkAsync(Id, linkName, linkUrl);
        if (!result.IsSuccess)
        {
            linkError = result.Errors.FirstOrDefault()
                ?? result.ValidationErrors.Select(v => v.ErrorMessage).FirstOrDefault()
                ?? "Could not add the link.";
            return;
        }

        isAuthorLinkModalOpen = false;
        linkName = string.Empty;
        linkUrl = string.Empty;
        await ReloadDetailAsync();
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

    private void ToggleSelectMode()
    {
        selectMode = !selectMode;
        if (!selectMode)
        {
            selectedBookIds.Clear();
        }
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
}