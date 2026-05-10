namespace Shelfwarden.Components.Pages;

public partial class Home : ComponentBase
{
    private DashboardDto? dashboard;

    protected override async Task OnInitializedAsync()
    {
        var result = await DashboardService.GetAsync();
        dashboard = result.IsSuccess
            ? result.Value
            : new DashboardDto(
                new DashboardStatsDto(0, 0, 0, 0, 0),
                [],
                []);
    }
}