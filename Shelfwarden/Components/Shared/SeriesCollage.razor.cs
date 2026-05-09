namespace Shelfwarden.Components.Shared;

public partial class SeriesCollage : ComponentBase
{
    [Parameter, EditorRequired]
    public IReadOnlyList<SeriesCoverDto>? Covers { get; set; }
}