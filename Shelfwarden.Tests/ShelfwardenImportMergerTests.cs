using Shelfwarden.Services.Scanning;

namespace Shelfwarden.Tests;

public class ShelfwardenImportMergerTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, null, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, null, false)]
    public void ResolveUseFileNameForTitle_sidecar_overrides_shelf_default(
        bool shelfDefault, bool? sidecarValue, bool expected)
    {
        Assert.Equal(expected, ShelfwardenImportMerger.ResolveUseFileNameForTitle(shelfDefault, sidecarValue));
    }

    [Fact]
    public void MergeList_sidecar_replace_overrides_shelf_cleared_authors()
    {
        var sidecar = new ShelfwardenImportField
        {
            Mode = ImportFieldMode.Replace,
            Values = ["Jane Austen"],
        };

        var result = ShelfwardenImportMerger.MergeList([], sidecar);

        Assert.Equal(["Jane Austen"], result);
    }

    [Fact]
    public void MergeList_sidecar_append_adds_to_shelf_cleared_genres()
    {
        var sidecar = new ShelfwardenImportField
        {
            Mode = ImportFieldMode.Append,
            Values = ["Fantasy"],
        };

        var result = ShelfwardenImportMerger.MergeList([], sidecar);

        Assert.Equal(["Fantasy"], result);
    }

    [Fact]
    public void MergeCollection_returns_trimmed_name_for_sidecar_override()
    {
        Assert.Equal("Curated", ShelfwardenImportMerger.MergeCollection("  Curated  "));
    }
}
