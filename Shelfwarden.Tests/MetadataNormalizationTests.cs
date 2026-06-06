using Shelfwarden.Services.Metadata;

namespace Shelfwarden.Tests;

public class MetadataNormalizationTests
{
    [Theory]
    [InlineData("978-0-7432-7356-7", "9780743273567")]
    [InlineData("0 8044 2957 X", "080442957X")]
    [InlineData("080442957x", "080442957X")]
    [InlineData("", "")]
    public void NormalizeIsbn_strips_formatting(string input, string expected)
    {
        Assert.Equal(expected, MetadataNormalization.NormalizeIsbn(input));
    }

    [Theory]
    [InlineData("<p>Hello <b>world</b></p>", "Hello world")]
    [InlineData("Tom &amp; Jerry", "Tom & Jerry")]
    [InlineData("Line<br/>break", "Line break")]
    public void StripHtml_removes_tags_and_decodes_entities(string input, string expected)
    {
        Assert.Equal(expected, MetadataNormalization.StripHtml(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void StripHtml_returns_null_for_blank(string? input)
    {
        Assert.Null(MetadataNormalization.StripHtml(input));
    }

    [Fact]
    public void ParseDate_handles_full_date()
    {
        var result = MetadataNormalization.ParseDate("2004-09-21");
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2004, 9, 21, 0, 0, 0, DateTimeKind.Utc), result!.Value);
    }

    [Fact]
    public void ParseDate_handles_year_only()
    {
        var result = MetadataNormalization.ParseDate("2004");
        Assert.NotNull(result);
        Assert.Equal(2004, result!.Value.Year);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    public void ParseDate_returns_null_for_unparseable(string? input)
    {
        Assert.Null(MetadataNormalization.ParseDate(input));
    }
}
