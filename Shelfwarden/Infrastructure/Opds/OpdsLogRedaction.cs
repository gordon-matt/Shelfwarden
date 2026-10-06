using System.Text.RegularExpressions;

namespace Shelfwarden.Infrastructure.Opds;

/// <summary>Removes OPDS keys embedded in request paths before they reach the logs.</summary>
public static partial class OpdsLogRedaction
{
    public static string RedactPath(string path) =>
        path.Contains("/opds/key/", StringComparison.OrdinalIgnoreCase)
            ? KeySegment().Replace(path, "$1***")
            : path;

    [GeneratedRegex("(/opds/key/)[^/?#]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeySegment();
}
