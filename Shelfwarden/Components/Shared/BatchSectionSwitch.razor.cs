namespace Shelfwarden.Components.Shared;

public partial class BatchSectionSwitch : ComponentBase
{
    private readonly string switchId = $"batch-section-{Guid.NewGuid():N}";

    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public bool Enabled { get; set; }
    [Parameter] public EventCallback<bool> EnabledChanged { get; set; }
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    private async Task OnEnabledChanged(ChangeEventArgs e)
    {
        Enabled = (bool)(e.Value ?? false);
        if (EnabledChanged.HasDelegate)
        {
            await EnabledChanged.InvokeAsync(Enabled);
        }
    }
}