namespace Shelfwarden.Components.Pages;

public partial class AuthorUnknown : ComponentBase
{
    private AuthorDetailDto? author;
    private bool loadFailed;

    private bool selectMode;
    private readonly HashSet<int> selectedBookIds = [];

    protected override async Task OnParametersSetAsync()
    {
        author = null;
        loadFailed = false;
        selectMode = false;
        selectedBookIds.Clear();

        var result = await AuthorService.GetUnknownAuthorDetailAsync();
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
        NavigationManager.NavigateTo($"books/batch-edit?ids={ids}&return=authors/unknown");
    }
}