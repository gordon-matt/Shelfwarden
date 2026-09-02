using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Pages;

public partial class SeriesIndex : ComponentBase
{
    private bool addSeriesBooksToUniverse;
    private string? editError;
    private bool editModalOpen;
    private bool editSaving;
    private int editSeriesId;
    private string editSeriesName = string.Empty;

    // 0 means "no universe" — <select> can't bind a nullable int cleanly.
    private int editUniverseId;

    private int originalUniverseId;
    private string query = string.Empty;
    private CancellationTokenSource? searchCts;
    private IReadOnlyList<SeriesListItemDto>? series;
    private IReadOnlyList<UniverseOptionDto> universeOptions = [];

    protected override async Task OnInitializedAsync()
    {
        var universesTask = UniverseService.SearchAsync();
        await LoadAsync();

        var universes = await universesTask;
        universeOptions = universes.IsSuccess ? universes.Value : [];
    }

    private void CloseEditModal()
    {
        editModalOpen = false;
        editSaving = false;
        editError = null;
    }

    private async Task ConfirmDeleteSeriesAsync(SeriesListItemDto s)
    {
        bool ok = await JS.InvokeAsync<bool>(
            "confirm",
            $"Delete series \"{s.Name}\"? Books stay in the library but are removed from this series.");
        if (!ok)
        {
            return;
        }

        var result = await SeriesService.DeleteAsync(s.Id);
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;

        var result = await SeriesService.ListAsync(query, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        series = result.IsSuccess ? result.Value : [];
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
            if (token.IsCancellationRequested)
            {
                return;
            }

            await LoadAsync();
        }
        catch (TaskCanceledException) { }
    }

    private void OpenEditModal(SeriesListItemDto s)
    {
        editSeriesId = s.Id;
        editSeriesName = s.Name;
        editUniverseId = s.UniverseId ?? 0;
        originalUniverseId = editUniverseId;
        addSeriesBooksToUniverse = false;
        editError = null;
        editModalOpen = true;
    }

    private async Task SaveEditAsync()
    {
        editSaving = true;
        editError = null;
        try
        {
            var result = await SeriesService.UpdateAsync(editSeriesId, editSeriesName);
            if (!result.IsSuccess)
            {
                editError = result.Errors.FirstOrDefault() ?? "Could not rename the series.";
                return;
            }

            if (editUniverseId != originalUniverseId)
            {
                var universeResult = await UniverseService.SetSeriesUniverseAsync(
                    editSeriesId,
                    editUniverseId > 0 ? editUniverseId : null,
                    addSeriesBooksToUniverse);
                if (!universeResult.IsSuccess)
                {
                    editError = universeResult.Errors.FirstOrDefault() ?? "Could not set the universe.";
                    return;
                }
            }

            CloseEditModal();
            await LoadAsync();
        }
        finally
        {
            editSaving = false;
        }
    }
}
