using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Pages;

public partial class Universes : ComponentBase
{
    private bool busy;
    private bool creating;
    private string? errorMessage;
    private CreateModel newUniverse = new();
    private string query = string.Empty;
    private CancellationTokenSource? searchCts;
    private IReadOnlyList<UniverseDto>? universes;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private void CancelCreate()
    {
        creating = false;
        errorMessage = null;
    }

    private async Task CreateAsync()
    {
        busy = true;
        errorMessage = null;
        try
        {
            var result = await UniverseService.CreateAsync(new CreateUniverseRequest
            {
                Name = newUniverse.Name,
                Description = newUniverse.Description,
            });

            if (result.IsSuccess)
            {
                creating = false;
                SidebarNavRefresh.NotifyNavigationDataChanged();
                NavigationManager.NavigateTo($"universes/{result.Value.Id}");
            }
            else
            {
                errorMessage = result.Errors.FirstOrDefault() ?? "Could not create the universe.";
            }
        }
        finally
        {
            busy = false;
        }
    }

    private async Task LoadAsync()
    {
        searchCts?.Cancel();
        searchCts = new CancellationTokenSource();
        var token = searchCts.Token;

        var result = await UniverseService.ListAsync(query, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        universes = result.IsSuccess ? result.Value : [];
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

    private void ShowCreate()
    {
        creating = true;
        errorMessage = null;
        newUniverse = new CreateModel();
    }

    private sealed class CreateModel
    {
        public string? Description { get; set; }

        [Required, StringLength(256)]
        public string Name { get; set; } = string.Empty;
    }
}
