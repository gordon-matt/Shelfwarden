using Shelfwarden.Services.Tts;

namespace Shelfwarden.Tests;

public class PdfTextNormalizerTests
{
    [Fact]
    public void Joins_hyphenated_word_wrapped_across_lines()
    {
        const string input = "Four small children clus-\ntered about her, and a baby filled her broad lap.";

        string result = PdfTextNormalizer.Normalize(input);

        Assert.Contains("clustered about her", result);
        Assert.DoesNotContain("clus-", result);
        Assert.DoesNotContain("\n", result);
    }

    [Fact]
    public void Collapses_soft_line_wraps_into_spaces()
    {
        const string input = "1743 and a fine June morning. Blue water,\nwind from the southwest, and Marguerite\nLedoux taking her last sight of Marblehead.";

        string result = PdfTextNormalizer.Normalize(input);

        Assert.Equal(
            "1743 and a fine June morning. Blue water, wind from the southwest, and Marguerite Ledoux taking her last sight of Marblehead.",
            result);
    }

    [Fact]
    public void Keeps_hyphen_for_uppercase_continuation_compound()
    {
        const string input = "He studied the Anglo-\nSaxon settlements closely.";

        string result = PdfTextNormalizer.Normalize(input);

        Assert.Contains("Anglo-Saxon settlements", result);
    }

    [Fact]
    public void Preserves_paragraph_breaks_on_blank_lines()
    {
        const string input = "First paragraph wraps\nover two lines.\n\nSecond paragraph here.";

        string result = PdfTextNormalizer.Normalize(input);

        Assert.Equal("First paragraph wraps over two lines.\nSecond paragraph here.", result);
    }

    [Fact]
    public void Removes_embedded_soft_hyphens()
    {
        const string input = "some\u00ADthing";

        string result = PdfTextNormalizer.Normalize(input);

        Assert.Equal("something", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Returns_empty_for_blank_input(string? input)
    {
        Assert.Equal(string.Empty, PdfTextNormalizer.Normalize(input));
    }

    [Fact]
    public void Does_not_join_when_hyphen_follows_non_letter()
    {
        const string input = "see page 10 -\nthen continue";

        string result = PdfTextNormalizer.Normalize(input);

        Assert.Contains("10 -", result);
    }
}
