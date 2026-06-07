using Shelfwarden.Extensions;

namespace Shelfwarden.Tests;

public class AuthorNameNormalizationTests
{
    [Theory]
    [InlineData("Smith, John", "John Smith")]
    [InlineData("van den Berg, Johan", "Johan van den Berg")]
    [InlineData("  Smith, John  ", "John Smith")]
    [InlineData("John Smith", "John Smith")]
    [InlineData("  John Smith  ", "John Smith")]
    public void NormalizeAuthorName_reverses_last_first_format(string input, string expected)
    {
        Assert.Equal(expected, input.NormalizeAuthorName());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Smith,")]
    public void NormalizeAuthorName_leaves_edge_cases_unchanged(string input)
    {
        Assert.Equal(input.Trim(), input.NormalizeAuthorName());
    }
}
