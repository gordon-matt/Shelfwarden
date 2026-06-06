using Shelfwarden.Services.Scanning;

namespace Shelfwarden.Tests;

public class ShelfScanFileFilterTests
{
    [Theory]
    [InlineData(@"D:\Library\Author\Title\book.epub", true)]
    [InlineData(@"D:\Library\Calibre Library\Author\Title (1)\book.epub", true)]
    [InlineData(@"D:\Library\.caltrash\Author\Title\book.epub", false)]
    [InlineData(@"D:\Library\.calnotes\Author\Title\book.epub", false)]
    [InlineData(@"D:\Library\Author\.caltrash\book.epub", false)]
    [InlineData(@"D:\Library\Author\.CALTRASH\book.pdf", false)]
    public void ShouldInclude_skips_calibre_hidden_folders(string path, bool expected)
    {
        Assert.Equal(expected, ShelfScanFileFilter.ShouldInclude(path));
    }
}
