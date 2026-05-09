namespace Shelfwarden.Components.Shared;

public partial class ShelfTileBanner : ComponentBase
{
    [Parameter, EditorRequired] public CardBannerPreview Preview { get; set; } = default!;

    [Parameter, EditorRequired] public string FallbackIconClass { get; set; } = "bi bi-bookshelf";
}