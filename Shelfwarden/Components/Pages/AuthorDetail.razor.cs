namespace Shelfwarden.Components.Pages;

public partial class AuthorDetail : ComponentBase
{
    [Parameter] public int Id { get; set; }

    private AuthorDetailDto? author;
    private bool loadFailed;

    private bool selectMode;
    private readonly HashSet<int> selectedBookIds = [];
    private bool isAuthorEditModalOpen;
    private readonly Dictionary<int, long> authorPhotoVersions = [];

    protected override async Task OnParametersSetAsync()
    {
        author = null;
        loadFailed = false;
        selectMode = false;
        selectedBookIds.Clear();
        isAuthorEditModalOpen = false;

        var result = await AuthorService.GetDetailAsync(Id);
        if (result.IsSuccess)
        {
            author = result.Value;
        }
        else
        {
            loadFailed = true;
        }
    }

    private void ToggleSelectMode()
    {
        selectMode = !selectMode;
        if (!selectMode) selectedBookIds.Clear();
    }

    private void ToggleSelection(int id, bool include)
    {
        if (include) selectedBookIds.Add(id);
        else selectedBookIds.Remove(id);
    }

    private void SelectAll()
    {
        if (author is null) return;
        foreach (var b in author.StandaloneBooks) selectedBookIds.Add(b.Id);
    }

    private void ClearSelection() => selectedBookIds.Clear();

    private void GoToBatchEdit()
    {
        if (selectedBookIds.Count == 0) return;
        string ids = string.Join(',', selectedBookIds);
        NavigationManager.NavigateTo($"books/batch-edit?ids={ids}&return=authors/{Id}");
    }

    private void OpenAuthorEditModal()
    {
        isAuthorEditModalOpen = true;
    }

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
}