using Microsoft.Extensions.Logging.Abstractions;
using Shelfwarden.Services.Scanning;

namespace Shelfwarden.Tests;

public class EpubCoverExtractionTests
{
    public static TheoryData<string> ProblemEpubFiles =>
    [
        @"D:\_Apps\Shelfwarden\Library\Non-Fiction\The Great Divorce.epub",
        @"D:\_Apps\Shelfwarden\Library\Non-Fiction\Studies in Medieval and Renaissance Literature.epub",
        @"D:\_Apps\Shelfwarden\Library\Non-Fiction\03 - A Sea of Skulls.epub",
    ];

    [Theory]
    [MemberData(nameof(ProblemEpubFiles))]
    public async Task ExtractAsync_finds_cover_for_problem_epubs(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var extractor = new EpubMetadataExtractor(NullLogger<EpubMetadataExtractor>.Instance);
        var metadata = await extractor.ExtractAsync(filePath);

        Assert.NotNull(metadata.Cover);
        Assert.NotEmpty(metadata.Cover.Data);
        Assert.False(string.IsNullOrWhiteSpace(metadata.Cover.Extension));
    }
}
