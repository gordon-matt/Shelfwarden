using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Pages;

public partial class Metadata : ComponentBase
{
    private enum MetadataTab
    {
        Genres,
        BookTags,
        ExtraContentTags,
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

    protected override async Task OnInitializedAsync() => await LoadGenresAsync();

    private void SetTab(MetadataTab tab)
    {
        activeTab = tab;
        error = null;
    }

    private Task OnTagManagerErrorAsync(string? message)
    {
        error = message;
        return Task.CompletedTask;
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

    private void ToggleGenreSelection(int genreId, bool include)
    {
        if (include)
        {
            selectedGenreIds.Add(genreId);
        }
        else
        {
            selectedGenreIds.Remove(genreId);
        }
    }

    private void ClearGenreSelection() => selectedGenreIds.Clear();

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

        var result = await GenreService.DeleteManyAsync(selectedGenreIds.ToList());
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
            return;
        }

        selectedGenreIds.Clear();
        error = null;
        await LoadGenresAsync();
    }

    private async Task RemoveUnusedGenresAsync()
    {
        if (!await JSRuntime.InvokeAsync<bool>("shelfwarden.confirmDialog",
                "Remove all genres not assigned to any book? This cannot be undone."))
        {
            return;
        }

        var result = await GenreService.DeleteUnusedAsync();
        if (!result.IsSuccess)
        {
            error = FormatResult(result);
            return;
        }

        selectedGenreIds.Clear();
        error = null;
        await LoadGenresAsync();
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

    private void CloseGenreMergeModal()
    {
        genreMergeModalOpen = false;
        genreMergeCandidates = [];
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

    private static string FormatResult(Ardalis.Result.Result result) => result.ValidationErrors is not null && result.ValidationErrors.Any()
            ? string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage))
            : string.Join(" ", result.Errors);

    private static string FormatResult<T>(Ardalis.Result.Result<T> result) => result.ValidationErrors is not null && result.ValidationErrors.Any()
            ? string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage))
            : string.Join(" ", result.Errors);
}
