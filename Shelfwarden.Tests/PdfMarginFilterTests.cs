using Shelfwarden.Services.Tts;

namespace Shelfwarden.Tests;

public class PdfMarginFilterTests
{
    private static readonly string[] Words =
    [
        "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel",
        "india", "juliet", "kilo", "lima", "mike", "november", "oscar", "papa",
    ];

    private static List<string> BuildPages(int count, Func<int, string> render)
        => Enumerable.Range(0, count).Select(render).ToList();

    [Fact]
    public void Detects_and_strips_running_head_and_page_number()
    {
        // Running head on top, genuinely unique body lines in the middle, page number at the bottom.
        var pages = BuildPages(12, i =>
            $"MARBLEHEAD\nThe {Words[i]} wind carried the ship onward.\nShe watched the {Words[i]} shore recede.\n{i + 15}");

        var patterns = PdfMarginFilter.Detect(pages);

        Assert.Contains("marblehead", patterns.Headers);
        Assert.Contains("#", patterns.Footers);

        string cleaned = PdfMarginFilter.StripMarginals(pages[0], patterns);

        Assert.DoesNotContain("MARBLEHEAD", cleaned);
        Assert.DoesNotContain("15", cleaned);
        Assert.Contains("carried the ship onward", cleaned);
        Assert.Contains("shore recede", cleaned);
    }

    [Fact]
    public void Strips_alternating_verso_recto_headers_with_page_numbers()
    {
        // Verso pages: "<n> THE SHERWOOD RING"; recto pages: "THE PEACEFUL SHORE <n>".
        var pages = BuildPages(16, i => i % 2 == 0
            ? $"{i} THE SHERWOOD RING\nThe {Words[i]} morning broke over the water."
            : $"THE PEACEFUL SHORE {i}\nA {Words[i]} silence settled on the deck.");

        var patterns = PdfMarginFilter.Detect(pages);
        string versoCleaned = PdfMarginFilter.StripMarginals(pages[2], patterns);
        string rectoCleaned = PdfMarginFilter.StripMarginals(pages[3], patterns);

        Assert.DoesNotContain("SHERWOOD RING", versoCleaned);
        Assert.DoesNotContain("PEACEFUL SHORE", rectoCleaned);
        Assert.Contains("morning broke over the water", versoCleaned);
        Assert.Contains("silence settled on the deck", rectoCleaned);
    }

    [Fact]
    public void Strips_bare_roman_numeral_folio()
    {
        var pages = BuildPages(10, i => $"Preface heading\nSome {Words[i]} front matter appears here.\nvii");

        var patterns = PdfMarginFilter.Detect(pages);
        string cleaned = PdfMarginFilter.StripMarginals("Chapter opens here.\nMore prose.\nvii", patterns);

        Assert.DoesNotContain("vii", cleaned);
        Assert.Contains("Chapter opens here", cleaned);
    }

    [Fact]
    public void Does_not_strip_unique_content_lines()
    {
        // Every line is unique across pages, so nothing should be treated as chrome.
        var pages = BuildPages(10, i => $"The {Words[i]} opening.\nMidway through {Words[i]}.\nThe {Words[i]} ending.");

        var patterns = PdfMarginFilter.Detect(pages);

        Assert.True(patterns.IsEmpty);

        string cleaned = PdfMarginFilter.StripMarginals(pages[3], patterns);
        Assert.Equal(pages[3], cleaned);
    }

    [Fact]
    public void Does_not_strip_single_letter_line()
    {
        // "I" is the pronoun, not a folio, and must survive even when it recurs near a margin.
        var pages = BuildPages(8, i => $"A {Words[i]} chapter starts.\nI");
        var patterns = PdfMarginFilter.Detect(pages);

        string cleaned = PdfMarginFilter.StripMarginals("A chapter starts.\nI", patterns);
        Assert.Contains("I", cleaned);
    }

    [Fact]
    public void Normalizes_page_number_variants_to_same_key()
    {
        Assert.Equal("marblehead #", PdfMarginFilter.NormalizeForMatch("MARBLEHEAD • 15"));
        Assert.Equal("marblehead #", PdfMarginFilter.NormalizeForMatch("Marblehead - 172"));
        Assert.Equal("#", PdfMarginFilter.NormalizeForMatch("  42  "));
    }
}
