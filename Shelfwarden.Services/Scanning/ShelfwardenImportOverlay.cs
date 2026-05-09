using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shelfwarden.Services.Scanning;

internal static class ShelfwardenImportConstants
{
    public const string FileName = "shelfwarden_import.json";
}

/// <summary>Optional per-folder sidecar for shelf scans. When present, merged into extracted
/// metadata before authors, series, genres, and tags are persisted.</summary>
internal sealed class ShelfwardenImportOverlay
{
    /// <summary>When true, scanner uses the ebook file name (without extension) as title.</summary>
    public bool? UseFileNameForTitle { get; set; }

    /// <summary>Optional collection name. If set, scanner creates the global collection if needed and adds the book to it.</summary>
    public string? Collection { get; set; }

    public ShelfwardenImportField? Author { get; set; }

    /// <summary>When set to a non-empty string, the book is assigned to that series (replaces any value from the file). Whitespace-only is ignored.</summary>
    public string? Series { get; set; }

    public ShelfwardenImportField? Genres { get; set; }

    public ShelfwardenImportField? Tags { get; set; }
}

internal sealed class ShelfwardenImportField
{
    public ImportFieldMode Mode { get; set; }

    public List<string>? Values { get; set; }
}

internal enum ImportFieldMode
{
    Replace,

    Append,
}

internal static class ShelfwardenImportJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };
}

internal static class ShelfwardenImportMerger
{
    public static bool UseFileNameForTitle(bool? value) => value is true;

    public static string? MergeCollection(string? collectionName)
        => string.IsNullOrWhiteSpace(collectionName) ? null : collectionName.Trim();

    public static IReadOnlyList<string> MergeList(IReadOnlyList<string> fromFile, ShelfwardenImportField? field)
    {
        if (field is null)
        {
            return fromFile;
        }

        var cleaned = CleanValues(field.Values);
        return cleaned.Count == 0
            ? fromFile
            : field.Mode switch
            {
                ImportFieldMode.Replace => cleaned,
                ImportFieldMode.Append => AppendDistinct(fromFile, cleaned),
                _ => fromFile,
            };
    }

    /// <summary>Sidecar <c>series</c> is a single string; when non-empty it overrides extracted metadata.</summary>
    public static string? MergeSeries(string? fromFile, string? seriesFromSidecar) => string.IsNullOrWhiteSpace(seriesFromSidecar) ? fromFile : seriesFromSidecar.Trim();

    private static List<string> CleanValues(IEnumerable<string>? values) => values is null
            ? []
            : values
            .Select(v => v?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Cast<string>()
            .ToList();

    private static List<string> AppendDistinct(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string x in first)
        {
            if (string.IsNullOrWhiteSpace(x))
            {
                continue;
            }

            string t = x.Trim();
            if (set.Add(t))
            {
                result.Add(t);
            }
        }

        foreach (string x in second)
        {
            if (string.IsNullOrWhiteSpace(x))
            {
                continue;
            }

            string t = x.Trim();
            if (set.Add(t))
            {
                result.Add(t);
            }
        }

        return result;
    }
}

internal static class ShelfwardenImportPath
{
    /// <summary>Walks from the file's directory toward <paramref name="shelfFolderRoot"/> and returns
    /// the first <c>shelfwarden_import.json</c> found (most specific / nearest to the file).</summary>
    public static string? FindNearestImportJsonPath(string filePath, string shelfFolderRoot)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }

        string rootFull = Path.GetFullPath(shelfFolderRoot);
        string rootNorm = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        while (!string.IsNullOrEmpty(dir))
        {
            string dirFull = Path.GetFullPath(dir);
            if (!IsUnderRoot(dirFull, rootNorm))
            {
                break;
            }

            string candidate = Path.Combine(dirFull, ShelfwardenImportConstants.FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dirFull);
            dir = parent?.FullName;
        }

        return null;
    }

    private static bool IsUnderRoot(string pathFull, string rootNorm)
    {
        string pathNorm = pathFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(pathNorm, rootNorm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = rootNorm + Path.DirectorySeparatorChar;
        return pathFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || pathFull.StartsWith(rootNorm + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}