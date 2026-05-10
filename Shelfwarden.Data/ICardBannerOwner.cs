namespace Shelfwarden.Data;

/// <summary>
/// Implemented by entities (Shelf, Collection, ReadingList) that surface a "tile header" image
/// on their list-page card. The three properties hold the user's choice for that card:
/// <list type="bullet">
///   <item><see cref="CardBannerMode"/> — random covers, uploaded image, or hand-picked covers.</item>
///   <item><see cref="CardBannerImageFileName"/> — relative file name when <see cref="CardBannerMode"/> is <see cref="CardHeaderBannerMode.UploadedImage"/>.</item>
///   <item><see cref="CardBannerBookIdsJson"/> — JSON-serialised int[] of book ids when the user picked specific covers.</item>
/// </list>
/// Only the banner-related state lives here; the ID type and other fields belong to the
/// concrete entity. The shared helpers in <c>Shelfwarden.Services.CardBannerSupport</c> use
/// this interface to apply updates uniformly across all three entity types.
/// </summary>
public interface ICardBannerOwner
{
    CardHeaderBannerMode CardBannerMode { get; set; }

    string? CardBannerImageFileName { get; set; }

    string? CardBannerBookIdsJson { get; set; }
}