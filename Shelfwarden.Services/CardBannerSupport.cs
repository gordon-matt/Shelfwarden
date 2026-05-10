using System.Text.Json;
using Shelfwarden.Services.Storage;

namespace Shelfwarden.Services;

/// <summary>
/// Tile / card header banner helpers, shared by <see cref="ShelfService"/>,
/// <see cref="CollectionService"/> and <see cref="ReadingListService"/>. Centralises the
/// validate-and-apply flow plus the (de)serialisation of selected book ids.
/// </summary>
internal static class CardBannerSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    internal readonly record struct BookCoverSource(int BookId);

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

        var covers = mode switch
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

    /// <summary>
    /// Validates the supplied banner mode + selected book ids against the entity's current
    /// state and (when valid) writes the result back onto <paramref name="owner"/>.
    /// </summary>
    /// <param name="kind">One of the Kind* constants (controls where files are stored).</param>
    /// <param name="entityId">Id of the entity being updated. Used for file path lookups.</param>
    /// <param name="owner">The entity (mutated in place when validation passes).</param>
    /// <param name="mode">The banner mode the caller wants to switch to.</param>
    /// <param name="selectedBookIds">Book ids the caller wants on the strip when <paramref name="mode"/> is <see cref="CardHeaderBannerMode.SelectedBooks"/>.</param>
    /// <param name="memberBookIds">Books actually inside the entity right now — selections outside this set are silently dropped.</param>
    /// <param name="storage">Storage provider for locating / deleting uploaded images.</param>
    /// <param name="modeFieldName">Field name for validation errors targeting <paramref name="mode"/>.</param>
    /// <param name="selectedBooksFieldName">Field name for validation errors targeting <paramref name="selectedBookIds"/>.</param>
    /// <param name="entityNoun">Human-friendly noun ("shelf" / "collection" / "reading list") used in error text.</param>
    internal static Result ApplyBannerUpdate(
        string kind,
        int entityId,
        ICardBannerOwner owner,
        CardHeaderBannerMode mode,
        IReadOnlyList<int> selectedBookIds,
        HashSet<int> memberBookIds,
        IStoragePathProvider storage,
        string modeFieldName,
        string selectedBooksFieldName,
        string entityNoun)
    {
        // Switching to "uploaded image" only makes sense if there is — or has ever been — an
        // uploaded file. Otherwise the user would land on a blank tile with nothing to show.
        if (mode == CardHeaderBannerMode.UploadedImage
            && string.IsNullOrEmpty(owner.CardBannerImageFileName)
            && storage.FindCardBannerFilePath(kind, entityId) is null)
        {
            return Result.Invalid(new ValidationError(
                modeFieldName,
                "Upload a banner image first, or choose another header option."));
        }

        var normalized = selectedBookIds.Where(memberBookIds.Contains).Take(CardBannerLimits.MaxStripCovers).ToList();
        if (mode == CardHeaderBannerMode.SelectedBooks && normalized.Count == 0)
        {
            return Result.Invalid(new ValidationError(
                selectedBooksFieldName,
                $"Pick up to {CardBannerLimits.MaxStripCovers} books from this {entityNoun} for the header."));
        }

        if (mode != CardHeaderBannerMode.UploadedImage)
        {
            // Switching away from the uploaded image — clean up the file so we don't leak it
            // on disk and so a future "upload" radio click forces a fresh upload.
            storage.DeleteCardBannerFile(kind, entityId);
            owner.CardBannerImageFileName = null;
        }

        owner.CardBannerMode = mode;
        owner.CardBannerBookIdsJson = mode == CardHeaderBannerMode.SelectedBooks
            ? SerializeBookIds(normalized)
            : null;

        return Result.Success();
    }

    /// <summary>
    /// Groups <paramref name="rows"/> by the <paramref name="ownerKey"/> selector into the
    /// shape <see cref="BuildPreview"/> wants for the candidates parameter. Callers fetch the
    /// raw rows however they like (single book query for shelves, joined queries for
    /// collections / reading lists).
    /// </summary>
    internal static Dictionary<int, List<BookCoverSource>> GroupCandidates<TRow>(
        IEnumerable<TRow> rows,
        Func<TRow, int> ownerKey,
        Func<TRow, int> bookIdSelector,
        Func<TRow, string?> coverPathSelector)
    {
        var dict = new Dictionary<int, List<BookCoverSource>>();
        foreach (var row in rows)
        {
            string? coverPath = coverPathSelector(row);
            if (string.IsNullOrEmpty(coverPath))
            {
                continue;
            }

            int key = ownerKey(row);
            if (!dict.TryGetValue(key, out var list))
            {
                list = [];
                dict[key] = list;
            }

            list.Add(new BookCoverSource(bookIdSelector(row)));
        }

        return dict;
    }

    private static IReadOnlyList<BookCoverRefDto> BuildSelectedCovers(
        IReadOnlyList<int> order,
        IReadOnlyList<BookCoverSource> candidates)
    {
        var byId = candidates.ToDictionary(c => c.BookId);
        var result = new List<BookCoverRefDto>();
        foreach (int id in order)
        {
            if (result.Count >= CardBannerLimits.MaxStripCovers)
            {
                break;
            }

            if (byId.TryGetValue(id, out var c))
            {
                result.Add(new BookCoverRefDto(c.BookId));
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

        return pool.Take(n).Select(c => new BookCoverRefDto(c.BookId)).ToList();
    }
}