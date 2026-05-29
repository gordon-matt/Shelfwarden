using System.Text.RegularExpressions;

namespace Shelfwarden.Services.Tts;

/// <summary>
/// Shared, dependency-free heuristics for classifying a section by its title (front matter,
/// chapter, back matter) and for recognising chapter headings in PDF body text. Both ebook
/// parsers reuse this so the "skip copyright / TOC / dedication" behaviour is identical
/// regardless of format.
/// </summary>
public static partial class SectionClassifier
{
    private static readonly string[] FrontMatterKeywords =
    [
        "copyright", "table of contents", "contents", "acknowledgement", "acknowledgment",
        "acknowledgements", "acknowledgments", "foreword", "preface", "dedication", "epigraph",
        "about the author", "also by", "title page", "half title", "series page", "praise for",
        "permissions", "legal notice", "disclaimer", "imprint", "colophon", "frontispiece",
        "cover", "copyright page",
    ];

    private static readonly string[] BackMatterKeywords =
    [
        "index", "bibliography", "references", "appendix", "glossary", "endnotes",
        "about the publisher", "afterword", "further reading", "author's note", "notes",
    ];

    /// <summary>
    /// Classifies <paramref name="title"/> into a <see cref="SectionKind"/>. Returns
    /// <see cref="SectionKind.Chapter"/> when nothing matches (the safe default — body content
    /// is included).
    /// </summary>
    public static SectionKind Classify(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return SectionKind.Chapter;
        }

        string t = title.Trim().ToLowerInvariant();

        // Match on whole-word boundaries where the keyword is a single token so a chapter
        // literally titled "Notes on a Scandal" isn't mistaken for an endnotes section.
        if (FrontMatterKeywords.Any(k => ContainsKeyword(t, k)))
        {
            return SectionKind.FrontMatter;
        }

        return BackMatterKeywords.Any(k => ContainsKeyword(t, k))
            ? SectionKind.BackMatter
            : SectionKind.Chapter;
    }

    /// <summary>
    /// Applies <see cref="Classify"/> to each section and pre-selects only the body chapters
    /// (front/back matter is left in the list but unticked so the user can re-add it).
    /// </summary>
    public static void ApplyDefaults(IEnumerable<BookSection> sections)
    {
        foreach (var section in sections)
        {
            section.Kind = Classify(section.Title);
            section.IsIncluded = section.Kind == SectionKind.Chapter;
        }
    }

    /// <summary>True when <paramref name="text"/> reads like a chapter / part heading.</summary>
    public static bool LooksLikeChapterHeading(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return ChapterHeadingRegex().IsMatch(text.Trim());
    }

    private static bool ContainsKeyword(string loweredTitle, string keyword)
    {
        // Multi-word keywords ("table of contents") are matched as substrings; single tokens
        // ("index", "notes") require a word boundary so they don't trip on longer words.
        if (keyword.Contains(' '))
        {
            return loweredTitle.Contains(keyword, StringComparison.Ordinal);
        }

        return Regex.IsMatch(loweredTitle, $@"\b{Regex.Escape(keyword)}\b");
    }

    [GeneratedRegex(
        @"^(chapter|part|book|section)\s+(\d+|[ivxlcdm]+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\b|^\d{1,3}[\.\):]\s+\w|^prologue|^epilogue|^introduction\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChapterHeadingRegex();
}
