using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Pages;

public partial class Authors : ComponentBase
{
    private IReadOnlyList<AuthorListItemDto>? authors;
    private int unknownBookCount;
    private IReadOnlyList<ShelfDto> shelves = [];
    private string shelfId = "";
    private string query = string.Empty;
    private CancellationTokenSource? searchCts;
    private readonly Dictionary<int, long> authorPhotoVersions = [];

    private bool isAuthorEditModalOpen;
    private int selectedAuthorId;
    private string selectedAuthorName = string.Empty;
    private string? selectedAuthorBiography;

    private bool selectMode;
    private readonly HashSet<int> selectedAuthorIds = [];

    private bool mergeModalOpen;
    private int mergePrimaryAuthorId;
    private List<AuthorListItemDto> mergeCandidates = [];

    protected override async Task OnInitializedAsync()
    {
        var shelvesResult = await ShelfService.GetAllAsync();
        if (shelvesResult.IsSuccess)
        {
            shelves = shelvesResult.Value;
        }

        await LoadAsync();
    }

    private static int? ParseShelfFilter(string raw)
        => string.IsNullOrWhiteSpace(raw) ? null : int.TryParse(raw, out int id) ? id : null;

    private async Task OnShelfFilterChangedAsync()
    {
        searchCts?.Cancel();
        await LoadAsync();
    }

    private async Task ClearShelfFilterAsync()
    {
        shelfId = "";
        await OnShelfFilterChangedAsync();
    }

    private async Task LoadAsync()
    {
        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;

        int? shelfFilter = ParseShelfFilter(shelfId);
        var listTask = AuthorService.ListAsync(query, shelfFilter, token);
        var unknownTask = AuthorService.GetBooksWithoutAuthorsCountAsync(shelfFilter, token);
        await Task.WhenAll(listTask, unknownTask);

        if (token.IsCancellationRequested) return;

        var lr = await listTask;
        var ur = await unknownTask;
        authors = lr.IsSuccess ? lr.Value : [];
        unknownBookCount = ur.IsSuccess ? ur.Value : 0;
    }

    private async Task OnQueryKeyUp(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await LoadAsync();
            return;
        }

        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;
        try
        {
            await Task.Delay(250, token);
            if (token.IsCancellationRequested) return;
            await LoadAsync();
        }
        catch (TaskCanceledException) { }
    }

    private void ToggleSelectMode()
    {
        selectMode = !selectMode;
        if (!selectMode) selectedAuthorIds.Clear();
    }

    private void ToggleSelection(int authorId, bool include)
    {
        if (include) selectedAuthorIds.Add(authorId);
        else selectedAuthorIds.Remove(authorId);
    }

    private void SelectAllListed()
    {
        if (authors is null) return;
        foreach (var a in authors) selectedAuthorIds.Add(a.Id);
    }

    private void ClearSelection() => selectedAuthorIds.Clear();

    private void HandleAuthorTileClick(int authorId)
    {
        if (!selectMode) return;
        ToggleSelection(authorId, !selectedAuthorIds.Contains(authorId));
    }

    private async Task DeleteSelectedAsync()
    {
        if (selectedAuthorIds.Count == 0) return;

        int n = selectedAuthorIds.Count;
        if (!await JSRuntime.InvokeAsync<bool>(
                "shelfwarden.confirmDialog",
                $"Delete {n} author record(s)? Every book link to those authors will be removed and the author entries deleted. This is not reversible."))
        {
            return;
        }

        var result = await AuthorService.DeleteAuthorsAsync(selectedAuthorIds.ToList());
        if (!result.IsSuccess)
        {
            await JSRuntime.InvokeVoidAsync("alert", result.Errors.FirstOrDefault() ?? "Delete failed.");
            return;
        }

        selectedAuthorIds.Clear();
        selectMode = false;
        await LoadAsync();
    }

    private Task OpenMergeModalAsync()
    {
        if (selectedAuthorIds.Count < 2 || authors is null)
        {
            return Task.CompletedTask;
        }

        mergeCandidates = authors.Where(a => selectedAuthorIds.Contains(a.Id)).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (mergeCandidates.Count < 2)
        {
            mergeCandidates = [];
            return Task.CompletedTask;
        }

        mergePrimaryAuthorId = mergeCandidates[0].Id;
        mergeModalOpen = true;
        return Task.CompletedTask;
    }

    private void CloseMergeModal()
    {
        mergeModalOpen = false;
        mergeCandidates = [];
    }

    private async Task ConfirmMergeAsync()
    {
        if (mergeCandidates.Count < 2) return;

        string primaryName = mergeCandidates.FirstOrDefault(c => c.Id == mergePrimaryAuthorId)?.Name ?? "Primary";
        var others = mergeCandidates.Where(c => c.Id != mergePrimaryAuthorId).Select(c => c.Id).ToList();

        if (!await JSRuntime.InvokeAsync<bool>(
                "shelfwarden.confirmDialog",
                $"Merge into \"{primaryName}\"? Other selected author records will be deleted. This is not reversible."))
        {
            return;
        }

        var result = await AuthorService.MergeAuthorsAsync(mergePrimaryAuthorId, others);
        if (!result.IsSuccess)
        {
            await JSRuntime.InvokeVoidAsync("alert", result.Errors.FirstOrDefault() ?? "Merge failed.");
            return;
        }

        CloseMergeModal();
        selectedAuthorIds.Clear();
        selectMode = false;
        await LoadAsync();
    }

    private async Task OpenAuthorMatchModalAsync(AuthorListItemDto authorRow)
    {
        selectedAuthorId = authorRow.Id;
        selectedAuthorName = authorRow.Name;
        selectedAuthorBiography = authorRow.Biography;
        isAuthorEditModalOpen = true;
    }

    private Task CloseAuthorEditModalAsync()
    {
        isAuthorEditModalOpen = false;
        return Task.CompletedTask;
    }

    private async Task OnAuthorSavedAsync()
    {
        authorPhotoVersions[selectedAuthorId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await LoadAsync();
    }

    private string GetAuthorPhotoUrl(int authorId)
    {
        long version = authorPhotoVersions.GetValueOrDefault(authorId, 0);
        return $"author-photos/{authorId}?v={version}";
    }
}