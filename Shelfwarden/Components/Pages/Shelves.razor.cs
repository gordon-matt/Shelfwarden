using Shelfwarden.Infrastructure;

namespace Shelfwarden.Components.Pages;

public partial class Shelves : ComponentBase
{
    private IReadOnlyList<ShelfDto>? shelves;
    private IReadOnlyDictionary<int, ScanStatusDto> statuses = new Dictionary<int, ScanStatusDto>();
    private readonly Dictionary<int, DateTime> optimisticBusyUntil = [];
    private CancellationTokenSource? pollCts;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        // Poll every 1.5s so the scan progress counters feel live without hammering Hangfire.
        pollCts = new CancellationTokenSource();
        _ = Task.Run(() => PollLoopAsync(pollCts.Token));
    }

    private async Task LoadAsync()
    {
        var libsTask = ShelfService.GetAllAsync();
        var statusTask = ScanStatusService.GetAllAsync();
        await Task.WhenAll(libsTask, statusTask);

        shelves = libsTask.Result.IsSuccess ? libsTask.Result.Value : [];
        if (statusTask.Result.IsSuccess)
        {
            statuses = statusTask.Result.Value;
        }
    }

    private async Task RefreshStatusesAsync()
    {
        var result = await ScanStatusService.GetAllAsync();
        if (!result.IsSuccess)
        {
            return;
        }

        var snapshot = result.Value;

        // If anything just transitioned out of running we want fresh book counts /
        // LastScannedAt — easier to just reload the shelves list once that happens.
        bool needShelfReload = statuses.Any(kv =>
            kv.Value.State == ScanState.Running
            && snapshot.TryGetValue(kv.Key, out var fresh)
            && fresh.State != ScanState.Running);

        statuses = snapshot;
        PruneOptimisticBusy();

        if (needShelfReload)
        {
            var libsResult = await ShelfService.GetAllAsync();
            if (libsResult.IsSuccess)
            {
                shelves = libsResult.Value;
            }
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task ScanAsync(int id)
    {
        optimisticBusyUntil[id] = DateTime.UtcNow.AddSeconds(20);
        await InvokeAsync(StateHasChanged);
        var scheduled = await ShelfService.ScheduleScanAsync(id);
        if (!scheduled.IsSuccess)
        {
            string detail = ResultMessages.UserFacing(scheduled, "Scan could not be scheduled.");
            ShelvesLogger.LogWarning(
                "[Shelves] ScheduleScanAsync failed ShelfId={ShelfId} Status={Status} DetailLen={Len} Detail={Detail} RawErrors=[{Errors}] Validation=[{Validation}]",
                id,
                scheduled.Status,
                detail.Length,
                detail,
                string.Join(" | ", scheduled.Errors.OfType<string>()),
                string.Join(" | ", scheduled.ValidationErrors.Select(v => v.ErrorMessage)));
            optimisticBusyUntil.Remove(id);
            await InvokeAsync(StateHasChanged);
            return;
        }
        // Optimistically reflect the queued state right away so the user sees feedback before
        // the next poll tick fires.
        var optimistic = new Dictionary<int, ScanStatusDto>(statuses)
        {
            [id] = new ScanStatusDto(id, ScanState.Queued, statuses.TryGetValue(id, out var s) ? s.LastScannedAt : null)
        };
        statuses = optimistic;
        StateHasChanged();
        await RefreshStatusesAsync();
    }

    private ScanStatusDto? GetStatus(int shelfId) =>
        statuses.TryGetValue(shelfId, out var s) ? s : null;

    private static bool IsBusy(ScanStatusDto? status) =>
        status is not null && (status.State == ScanState.Running || status.State == ScanState.Queued);

    private bool HasOptimisticBusy(int shelfId)
        => optimisticBusyUntil.TryGetValue(shelfId, out var until) && until > DateTime.UtcNow;

    private void PruneOptimisticBusy()
    {
        if (optimisticBusyUntil.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var keys = optimisticBusyUntil.Keys.ToList();
        foreach (int id in keys)
        {
            bool serverBusy = statuses.TryGetValue(id, out var s) && IsBusy(s);
            if (serverBusy)
            {
                continue;
            }

            if (optimisticBusyUntil[id] <= now)
            {
                optimisticBusyUntil.Remove(id);
            }
        }
    }

    private static RenderFragment RenderStatusBadge(ScanStatusDto? status) => __builder =>
    {
        if (status is null)
        {
            return;
        }

        switch (status.State)
        {
            case ScanState.Running:
                __builder.AddMarkupContent(0,
                    """<span class="badge bg-info"><span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span>Scanning</span>""");
                break;

            case ScanState.Queued:
                __builder.AddMarkupContent(0,
                    """<span class="badge bg-secondary">Queued</span>""");
                break;

            case ScanState.Idle:
                __builder.AddMarkupContent(0,
                    """<span class="badge bg-warning-subtle text-warning-emphasis">Never scanned</span>""");
                break;
        }
    };

    private static string FormatRelative(DateTime utc)
    {
        var delta = DateTime.UtcNow - utc;
        return delta < TimeSpan.FromMinutes(1)
            ? "just now"
            : delta < TimeSpan.FromHours(1)
            ? $"{(int)delta.TotalMinutes}m ago"
            : delta < TimeSpan.FromDays(1)
            ? $"{(int)delta.TotalHours}h ago"
            : delta < TimeSpan.FromDays(30) ? $"{(int)delta.TotalDays}d ago" : utc.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public void Dispose()
    {
        pollCts?.Cancel();
        pollCts?.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5), token);
                if (token.IsCancellationRequested)
                {
                    break;
                }

                await RefreshStatusesAsync();
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}