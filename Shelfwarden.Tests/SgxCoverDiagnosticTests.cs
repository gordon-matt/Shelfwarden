using Microsoft.Extensions.Logging.Abstractions;
using Shelfwarden.Services.Scanning;

namespace Shelfwarden.Tests;

public class SgxCoverDiagnosticTests
{
    [Fact]
    public async Task Extracts_cover_despite_malformed_ncx()
    {
        const string filePath = @"D:\_Apps\Shelfwarden\Library\Fiction\Test\SGX 05 - Wild Blue.epub";
        if (!File.Exists(filePath))
        {
            return;
        }

        var extractor = new EpubMetadataExtractor(NullLogger<EpubMetadataExtractor>.Instance);
        var metadata = await extractor.ExtractAsync(filePath);

        Assert.NotNull(metadata.Cover);
        Assert.NotEmpty(metadata.Cover!.Data);
    }
}
