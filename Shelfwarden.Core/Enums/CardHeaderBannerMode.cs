namespace Shelfwarden.Enums;

/// <summary>How a shelf / collection / reading list tile preview image is chosen.</summary>
public enum CardHeaderBannerMode : byte
{
    /// <summary>Up to <see cref="CardBannerLimits.MaxStripCovers"/> covers picked at random from books in the list (refreshes on each page load).</summary>
    RandomCovers = 0,

    /// <summary>A single user-uploaded image stored on the server.</summary>
    UploadedImage = 1,

    /// <summary>Up to <see cref="CardBannerLimits.MaxStripCovers"/> covers chosen explicitly by the user.</summary>
    SelectedBooks = 2,
}
