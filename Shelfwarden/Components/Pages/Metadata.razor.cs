using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Pages;

public partial class Metadata : ComponentBase
{
    private enum MetadataTab
    {
        Genres,
        Tags,
    }

    private MetadataTab activeTab = MetadataTab.Genres;
    private string? error;

    private IReadOnlyList<GenreDto>? genres;
    private string genreQuery = string.Empty;
    private CancellationTokenSource? genreSearchCts;
    private readonly HashSet<int> selectedGenreIds = [];
    private bool genreMergeModalOpen;
    private int genreMergeTargetId;
    private List<GenreDto> genreMergeCandidates = [];

    private IReadOnlyList<TagDto>? tags;
    private string tagQuery = string.Empty;
    private CancellationTokenSource? tagSearchCts;
    private readonly HashSet<int> selectedTagIds = [];
    private bool tagMergeModalOpen;
    private int tagMergeTargetId;
    private List<TagDto> tagMergeCandidates = [];

    protected override async Task OnInitializedAsync()
    {
        await Task.WhenAll(LoadGenresAsync(), LoadTagsAsync());
    }

    private void SetTab(MetadataTab tab)
    {
        activeTab = tab;
        error = null;
    }

    private async Task LoadGenresAsync()
    {
        var result = await GenreService.ListAsync(genreQuery);
        genres = result.IsSuccess ? result.Value : [];
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
        }
    }

    private async Task LoadTagsAsync()
    {
        var result = await TagService.ListAsync(tagQuery);
        tags = result.IsSuccess ? result.Value : [];
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
        }
    }

    private async Task OnGenreQueryKeyUp(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await LoadGenresAsync();
            return;
        }

        genreSearchCts?.Cancel();
        genreSearchCts = new CancellationTokenSource();
        var token = genreSearchCts.Token;
        try
        {
            await Task.Delay(250, token);
            if (!token.IsCancellationRequested)
            {
                await LoadGenresAsync();
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async Task OnTagQueryKeyUp(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await LoadTagsAsync();
            return;
        }

        tagSearchCts?.Cancel();
        tagSearchCts = new CancellationTokenSource();
        var token = tagSearchCts.Token;
        try
        {
            await Task.Delay(250, token);
            if (!token.IsCancellationRequested)
            {
                await LoadTagsAsync();
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void ToggleGenreSelection(int genreId, bool include)
    {
        if (include) selectedGenreIds.Add(genreId);
        else selectedGenreIds.Remove(genreId);
    }

    private void ToggleTagSelection(int tagId, bool include)
    {
        if (include) selectedTagIds.Add(tagId);
        else selectedTagIds.Remove(tagId);
    }

    private void ClearGenreSelection() => selectedGenreIds.Clear();

    private void ClearTagSelection() => selectedTagIds.Clear();

    private async Task OpenGenreCreateAsync()
    {
        string? name = await JSRuntime.InvokeAsync<string?>("prompt", "New genre name:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var result = await GenreService.CreateAsync(name);
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
            return;
        }

        error = null;
        await LoadGenresAsync();
    }

    private async Task OpenTagCreateAsync()
    {
        string? name = await JSRuntime.InvokeAsync<string?>("prompt", "New tag name:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var result = await TagService.CreateAsync(name);
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
            return;
        }

        error = null;
        await LoadTagsAsync();
    }

    private async Task OpenGenreRenameAsync(GenreDto genre)
    {
        string? name = await JSRuntime.InvokeAsync<string?>("prompt", "Rename genre:", genre.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), genre.Name, StringComparison.Ordinal))
        {
            return;
        }

        var result = await GenreService.UpdateAsync(genre.Id, name);
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
            return;
        }

        error = null;
        await LoadGenresAsync();
    }

    private async Task OpenTagRenameAsync(TagDto tag)
    {
        string? name = await JSRuntime.InvokeAsync<string?>("prompt", "Rename tag:", tag.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), tag.Name, StringComparison.Ordinal))
        {
            return;
        }

        var result = await TagService.UpdateAsync(tag.Id, name);
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
            return;
        }

        error = null;
        await LoadTagsAsync();
    }

    private async Task DeleteGenreAsync(GenreDto genre)
    {
        if (!await JSRuntime.InvokeAsync<bool>("shelfwarden.confirmDialog", $"Delete genre \"{genre.Name}\"? This cannot be undone."))
        {
            return;
        }

        var result = await GenreService.DeleteAsync(genre.Id);
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
            return;
        }

        selectedGenreIds.Remove(genre.Id);
        error = null;
        await LoadGenresAsync();
    }

    private async Task DeleteTagAsync(TagDto tag)
    {
        if (!await JSRuntime.InvokeAsync<bool>("shelfwarden.confirmDialog", $"Delete tag \"{tag.Name}\"? This cannot be undone."))
        {
            return;
        }

        var result = await TagService.DeleteAsync(tag.Id);
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
            return;
        }

        selectedTagIds.Remove(tag.Id);
        error = null;
        await LoadTagsAsync();
    }

    private async Task DeleteSelectedGenresAsync()
    {
        if (selectedGenreIds.Count == 0)
        {
            return;
        }

        if (!await JSRuntime.InvokeAsync<bool>("shelfwarden.confirmDialog", $"Delete {selectedGenreIds.Count} selected genre(s)? This cannot be undone."))
        {
            return;
        }

        foreach (int id in selectedGenreIds.ToList())
        {
            var result = await GenreService.DeleteAsync(id);
            if (!result.IsSuccess)
            {
                error = FormatResult(result);
                return;
            }
        }

        selectedGenreIds.Clear();
        error = null;
        await LoadGenresAsync();
    }

    private async Task DeleteSelectedTagsAsync()
    {
        if (selectedTagIds.Count == 0)
        {
            return;
        }

        if (!await JSRuntime.InvokeAsync<bool>("shelfwarden.confirmDialog", $"Delete {selectedTagIds.Count} selected tag(s)? This cannot be undone."))
        {
            return;
        }

        foreach (int id in selectedTagIds.ToList())
        {
            var result = await TagService.DeleteAsync(id);
            if (!result.IsSuccess)
            {
                error = FormatResult(result);
                return;
            }
        }

        selectedTagIds.Clear();
        error = null;
        await LoadTagsAsync();
    }

    private void OpenGenreMergeModal()
    {
        if (genres is null || selectedGenreIds.Count < 2)
        {
            return;
        }

        genreMergeCandidates = genres
            .Where(g => selectedGenreIds.Contains(g.Id))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (genreMergeCandidates.Count < 2)
        {
            genreMergeCandidates = [];
            return;
        }

        genreMergeTargetId = genreMergeCandidates[0].Id;
        genreMergeModalOpen = true;
    }

    private void OpenTagMergeModal()
    {
        if (tags is null || selectedTagIds.Count < 2)
        {
            return;
        }

        tagMergeCandidates = tags
            .Where(t => selectedTagIds.Contains(t.Id))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tagMergeCandidates.Count < 2)
        {
            tagMergeCandidates = [];
            return;
        }

        tagMergeTargetId = tagMergeCandidates[0].Id;
        tagMergeModalOpen = true;
    }

    private void CloseGenreMergeModal()
    {
        genreMergeModalOpen = false;
        genreMergeCandidates = [];
    }

    private void CloseTagMergeModal()
    {
        tagMergeModalOpen = false;
        tagMergeCandidates = [];
    }

    private async Task ConfirmGenreMergeAsync()
    {
        var sourceIds = genreMergeCandidates
            .Where(c => c.Id != genreMergeTargetId)
            .Select(c => c.Id)
            .ToList();
        if (sourceIds.Count == 0)
        {
            return;
        }

        string targetName = genreMergeCandidates.First(c => c.Id == genreMergeTargetId).Name;
        if (!await JSRuntime.InvokeAsync<bool>("shelfwarden.confirmDialog", $"Merge into \"{targetName}\"? Other selected genres will be deleted."))
        {
            return;
        }

        var result = await GenreService.MergeAsync(genreMergeTargetId, sourceIds);
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
            return;
        }

        CloseGenreMergeModal();
        selectedGenreIds.Clear();
        error = null;
        await LoadGenresAsync();
    }

    private async Task ConfirmTagMergeAsync()
    {
        var sourceIds = tagMergeCandidates
            .Where(c => c.Id != tagMergeTargetId)
            .Select(c => c.Id)
            .ToList();
        if (sourceIds.Count == 0)
        {
            return;
        }

        string targetName = tagMergeCandidates.First(c => c.Id == tagMergeTargetId).Name;
        if (!await JSRuntime.InvokeAsync<bool>("shelfwarden.confirmDialog", $"Merge into \"{targetName}\"? Other selected tags will be deleted."))
        {
            return;
        }

        var result = await TagService.MergeAsync(tagMergeTargetId, sourceIds);
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
            return;
        }

        CloseTagMergeModal();
        selectedTagIds.Clear();
        error = null;
        await LoadTagsAsync();
    }

    private static string FormatResult(Ardalis.Result.Result result)
    {
        if (result.ValidationErrors is not null && result.ValidationErrors.Any())
        {
            return string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage));
        }

        return string.Join(" ", result.Errors);
    }

    private static string FormatResult<T>(Ardalis.Result.Result<T> result)
    {
        if (result.ValidationErrors is not null && result.ValidationErrors.Any())
        {
            return string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage));
        }

        return string.Join(" ", result.Errors);
    }
}