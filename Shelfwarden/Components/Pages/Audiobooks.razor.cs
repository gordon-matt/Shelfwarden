using Microsoft.JSInterop;

namespace Shelfwarden.Components.Pages;

public partial class Audiobooks : ComponentBase, IDisposable
{
    private int? busyBookId;
    private string? error;
    private IReadOnlyList<AudiobookSummaryDto>? items;
    private bool loading = true;
    private int? playingBookId;
    private Timer? pollTimer;
    [Inject] private IAudiobookService AudiobookService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();

        // Poll while anything is queued or generating so progress and state stay fresh.
        pollTimer = new Timer(_ => _ = PollAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    private static string Describe(
        IEnumerable<ValidationError>? validationErrors,
        IEnumerable<string>? errors)
    {
        var validation = validationErrors?.Select(e => e.ErrorMessage).ToList() ?? [];
        if (validation.Count > 0)
        {
            return string.Join(" ", validation);
        }

        var general = errors?.ToList() ?? [];
        return general.Count > 0 ? string.Join(" ", general) : "Something went wrong. Check the server logs.";
    }

    private static string FormatDuration(double? seconds)
    {
        if (seconds is not > 0)
        {
            return "—";
        }

        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    private static string FormatResult(Result result) => Describe(result.ValidationErrors, result.Errors);

    private static string FormatResult<T>(Result<T> result) => Describe(result.ValidationErrors, result.Errors);

    private static string FormatSize(long? bytes)
    {
        if (bytes is not > 0)
        {
            return "—";
        }

        double mb = bytes.Value / 1024d / 1024d;
        return mb >= 1024 ? $"{mb / 1024d:0.0} GB" : $"{mb:0.0} MB";
    }

    private static (string Css, string Label) StatusBadge(AudiobookState state) => state switch
    {
        AudiobookState.Pending => ("text-bg-secondary", "Queued"),
        AudiobookState.Running => ("text-bg-primary", "Generating"),
        AudiobookState.Completed => ("text-bg-success", "Completed"),
        AudiobookState.Failed => ("text-bg-danger", "Failed"),
        _ => ("text-bg-light", "—"),
    };

    private async Task CancelAsync(AudiobookSummaryDto item)
    {
        string verb = item.State == AudiobookState.Failed ? "clear this failed generation" : "cancel this generation";
        if (!await JS.InvokeAsync<bool>("confirm", $"Are you sure you want to {verb} for \"{item.BookTitle}\"?"))
        {
            return;
        }

        busyBookId = item.BookId;
        try
        {
            var result = await AudiobookService.CancelAsync(item.BookId);
            if (!result.IsSuccess)
            {
                error = FormatResult(result);
            }

            if (playingBookId == item.BookId)
            {
                playingBookId = null;
            }

            await LoadAsync();
        }
        finally
        {
            busyBookId = null;
        }
    }

    private async Task DeleteAsync(AudiobookSummaryDto item)
    {
        if (!await JS.InvokeAsync<bool>("confirm",
            $"Delete the generated audiobook for \"{item.BookTitle}\"? This cannot be undone."))
        {
            return;
        }

        busyBookId = item.BookId;
        try
        {
            var result = await AudiobookService.DeleteAsync(item.BookId);
            if (!result.IsSuccess)
            {
                error = FormatResult(result);
            }

            if (playingBookId == item.BookId)
            {
                playingBookId = null;
            }

            await LoadAsync();
        }
        finally
        {
            busyBookId = null;
        }
    }

    private async Task LoadAsync()
    {
        var result = await AudiobookService.ListAllAsync();
        if (result.IsSuccess)
        {
            items = result.Value;
            error = null;
        }
        else
        {
            error = FormatResult(result);
            items ??= [];
        }

        loading = false;
    }

    private async Task PollAsync()
    {
        try
        {
            if (items is null || !items.Any(i => i.State is AudiobookState.Pending or AudiobookState.Running))
            {
                return;
            }

            await InvokeAsync(async () =>
            {
                await LoadAsync();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RefreshAsync()
    {
        loading = true;
        await LoadAsync();
    }

    private void TogglePlay(int bookId)
        => playingBookId = playingBookId == bookId ? null : bookId;

    public void Dispose()
    {
        pollTimer?.Dispose();
        pollTimer = null;
    }
}