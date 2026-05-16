using Shelfwarden.Services;

namespace Shelfwarden.Tests;

public class ExtrasScanFileFilterTests
{
    [Theory]
    [InlineData(@"D:\extras\author\map.pdf", true)]
    [InlineData(@"D:\extras\author\notes.epub", true)]
    [InlineData(@"D:\extras\Thumbs.db", false)]
    [InlineData(@"D:\extras\folder\Thumbs.db", false)]
    [InlineData(@"D:\extras\.DS_Store", false)]
    [InlineData(@"D:\extras\author\._hidden.pdf", false)]
    [InlineData(@"D:\extras\author\#recycle\doc.pdf", false)]
    [InlineData(@"D:\extras\author\@eadir\thumb.jpg", false)]
    [InlineData(@"D:\extras\@tmp\file.txt", false)]
    [InlineData(@"D:\extras\__MACOSX\author\._file.pdf", false)]
    [InlineData(@"D:\extras\.synologyworkingdirectory", false)]
    public void ShouldInclude_respects_system_and_nas_paths(string path, bool expected)
    {
        Assert.Equal(expected, ExtrasScanFileFilter.ShouldInclude(path));
    }
}
