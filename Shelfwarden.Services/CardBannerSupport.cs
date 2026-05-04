using System.Text.Json;
using Shelfwarden.Models;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services;

internal static class CardBannerSupport
{
    internal const string KindShelves = "shelves";
    internal const string KindCollections = "collections";
    internal const string KindReadingLists = "reading-lists";

    private static readonly JsonSerializerOptions JsonOptions = new();

    internal readonly record struct BookCoverSource(int BookId, long CoverCacheVersion);

    internal static IReadOnlyList<int> ParseBookIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            int[]? ids = JsonSerializer.Deserialize<int[]>(json, JsonOptions);
            return ids?.Where(i => i > 0).Distinct().Take(CardBannerLimits.MaxStripCovers).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    internal static string? SerializeBookIds(IReadOnlyList<int> ids)
    {
        var trimmed = ids.Where(i => i > 0).Distinct().Take(CardBannerLimits.MaxStripCovers).ToList();
        return trimmed.Count == 0 ? null : JsonSerializer.Serialize(trimmed, JsonOptions);
    }

    /// <summary>Builds preview for list tiles. <paramref name="candidates"/> are books with covers in this entity.</summary>
    internal static CardBannerPreview BuildPreview(
        CardHeaderBannerMode mode,
        string? storedImageFileName,
        string? selectedBookIdsJson,
        string urlKind,
        int entityId,
        IReadOnlyList<BookCoverSource> candidates,
        IStoragePathProvider storage)
    {
        string? uploadedUrl = null;
        if (mode == CardHeaderBannerMode.UploadedImage && !string.IsNullOrEmpty(storedImageFileName))
        {
            string? path = storage.GetCardBannerPath(storedImageFileName)
                ?? storage.FindCardBannerFilePath(urlKind, entityId);
            if (path is not null && File.Exists(path))
            {
                long v = File.GetLastWriteTimeUtc(path).Ticks;
                uploadedUrl = $"/card-banners/{urlKind}/{entityId}?v={v}";
            }
        }

        if (uploadedUrl is not null)
        {
            return new CardBannerPreview(CardHeaderBannerMode.UploadedImage, uploadedUrl, []);
        }

        IReadOnlyList<BookCoverRefDto> covers = mode switch
        {
            CardHeaderBannerMode.SelectedBooks => BuildSelectedCovers(ParseBookIds(selectedBookIdsJson), candidates),
            _ => PickRandomCovers(candidates, CardBannerLimits.MaxStripCovers),
        };

        return new CardBannerPreview(mode, null, covers);
    }

    internal static CardBannerSettingsDto BuildSettings(
        CardHeaderBannerMode mode,
        string? storedImageFileName,
        string? selectedBookIdsJson,
        string urlKind,
        int entityId,
        IStoragePathProvider storage)
    {
        var ids = ParseBookIds(selectedBookIdsJson);
        bool hasFile = !string.IsNullOrEmpty(storedImageFileName) &&
            (storage.GetCardBannerPath(storedImageFileName) is not null
             || storage.FindCardBannerFilePath(urlKind, entityId) is not null);

        return new CardBannerSettingsDto(mode, ids, hasFile);
    }

    private static IReadOnlyList<BookCoverRefDto> BuildSelectedCovers(
        IReadOnlyList<int> order,
        IReadOnlyList<BookCoverSource> candidates)
    {
        var byId = candidates.ToDictionary(c => c.BookId);
        var result = new List<BookCoverRefDto>();
        foreach (int id in order)
        {
            if (result.Count >= CardBannerLimits.MaxStripCovers) break;
            if (byId.TryGetValue(id, out var c))
            {
                result.Add(new BookCoverRefDto(c.BookId, c.CoverCacheVersion));
            }
        }

        return result;
    }

    private static IReadOnlyList<BookCoverRefDto> PickRandomCovers(IReadOnlyList<BookCoverSource> candidates, int max)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var pool = candidates.ToList();
        int n = Math.Min(max, pool.Count);
        // Fisher–Yates partial shuffle
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.Take(n).Select(c => new BookCoverRefDto(c.BookId, c.CoverCacheVersion)).ToList();
    }
}
