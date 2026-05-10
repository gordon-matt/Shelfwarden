namespace Shelfwarden.Components.Shared;

public partial class BatchChipMode : ComponentBase
{
    [Parameter] public ChipApplyMode Mode { get; set; }
    [Parameter] public EventCallback<ChipApplyMode> ModeChanged { get; set; }

    private async Task Set(ChipApplyMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        if (ModeChanged.HasDelegate)
        {
            await ModeChanged.InvokeAsync(mode);
        }
    }
}