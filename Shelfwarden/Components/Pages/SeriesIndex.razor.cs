using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Pages;

public partial class SeriesIndex : ComponentBase
{
    private string query = string.Empty;
    private string? renameError;
    private bool renameModalOpen;
    private bool renameSaving;
    private int renameSeriesId;
    private string renameSeriesName = string.Empty;
    private CancellationTokenSource? searchCts;
    private IReadOnlyList<SeriesListItemDto>? series;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private void CloseRenameModal()
    {
        renameModalOpen = false;
        renameSaving = false;
        renameError = null;
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

    private void OpenRenameModal(SeriesListItemDto s)
    {
        renameSeriesId = s.Id;
        renameSeriesName = s.Name;
        renameError = null;
        renameModalOpen = true;
    }

    private async Task SaveRenameAsync()
    {
        renameSaving = true;
        renameError = null;
        try
        {
            var result = await SeriesService.UpdateAsync(renameSeriesId, renameSeriesName);
            if (result.IsSuccess)
            {
                CloseRenameModal();
                await LoadAsync();
            }
            else
            {
                renameError = result.Errors.FirstOrDefault() ?? "Could not rename the series.";
            }
        }
        finally
        {
            renameSaving = false;
        }
    }
}