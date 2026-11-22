namespace Shelfwarden.Models;

/// <summary>Limits for cover strips on shelf / collection / reading-list tiles.</summary>
public static class CardBannerLimits
{
    /// <summary>
    /// Maximum covers in a tile strip (random or user-selected). Extra slices keep the strip
    /// edge-to-edge without wide gaps on typical card widths.
    /// </summary>
    public const int MaxStripCovers = 12;
}

/// <summary>Resolved data for rendering a tile banner (list views).</summary>
/// <param name="Mode">How the tile image is chosen.</param>
/// <param name="UploadedImageUrl">Address of an uploaded banner, such as <c>/card-banners/collections/3?v=…</c>, or <see langword="null"/>.</param>
/// <param name="CoverRefs">Book covers used when the mode is not an uploaded image.</param>
public record CardBannerPreview(
    CardHeaderBannerMode Mode,
    string? UploadedImageUrl,
    IReadOnlyList<BookCoverRefDto> CoverRefs);

/// <summary>Minimal data for a cover thumbnail URL.</summary>
/// <param name="BookId">Book whose cover to load.</param>
public record BookCoverRefDto(int BookId);

/// <summary>Settings loaded for edit forms (detail pages).</summary>
/// <param name="Mode">How the tile image is chosen.</param>
/// <param name="SelectedBookIds">Books chosen for <see cref="CardHeaderBannerMode.SelectedBooks"/>. Empty for the other modes.</param>
/// <param name="HasCustomImage"><see langword="true"/> when a banner file has been saved.</param>
public record CardBannerSettingsDto(
    CardHeaderBannerMode Mode,
    IReadOnlyList<int> SelectedBookIds,
    bool HasCustomImage);