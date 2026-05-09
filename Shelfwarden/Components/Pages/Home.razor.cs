namespace Shelfwarden.Components.Pages;

public partial class Home : ComponentBase
{
    private DashboardDto? dashboard;

    protected override async Task OnInitializedAsync()
    {
        var result = await DashboardService.GetAsync();
        if (result.IsSuccess)
        {
            dashboard = result.Value;
        }
        else
        {
            dashboard = new DashboardDto(
                new DashboardStatsDto(0, 0, 0, 0, 0),
                [],
                []);
        }
    }
}