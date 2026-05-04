namespace Shelfwarden.Models;

/// <summary>Resolved data for rendering a tile banner (list views).</summary>
public record CardBannerPreview(
    CardHeaderBannerMode Mode,
    /// <summary>Full URL path for a custom uploaded banner, e.g. <c>/card-banners/collections/3?v=…</c>.</summary>
    string? UploadedImageUrl,
    IReadOnlyList<BookCoverRefDto> CoverRefs);

/// <summary>Minimal data for a cover thumbnail URL.</summary>
public record BookCoverRefDto(int BookId, long CoverCacheVersion);

/// <summary>Settings loaded for edit forms (detail pages).</summary>
public record CardBannerSettingsDto(
    CardHeaderBannerMode Mode,
    IReadOnlyList<int> SelectedBookIds,
    /// <summary>True when a custom file has been saved for this entity.</summary>
    bool HasCustomImage);
