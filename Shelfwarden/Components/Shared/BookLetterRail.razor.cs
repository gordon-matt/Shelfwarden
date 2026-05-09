using Microsoft.AspNetCore.Components;

namespace Shelfwarden.Components.Shared;

public partial class BookLetterRail : ComponentBase
{
    /// <summary>Active rail key: empty string / null = All, <c>#</c>, or a single letter A–Z.</summary>
    [Parameter]
    public string? StartsWith { get; set; }

    [Parameter]
    public EventCallback<string?> StartsWithChanged { get; set; }

    private async Task OnSelectAsync(string value)
    {
        string? next;
        if (string.IsNullOrEmpty(value))
        {
            next = null;
        }
        else
        {
            next = string.Equals(StartsWith ?? string.Empty, value, StringComparison.Ordinal)
                ? null
                : value;
        }

        await StartsWithChanged.InvokeAsync(next);
    }
}