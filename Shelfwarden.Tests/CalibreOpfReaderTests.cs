using Microsoft.Extensions.Logging.Abstractions;
using Shelfwarden.Services.Scanning;

namespace Shelfwarden.Tests;

public class CalibreOpfReaderTests : IDisposable
{
    private readonly string tempDir;
    private readonly CalibreOpfReader reader = new(NullLogger<CalibreOpfReader>.Instance);

    public CalibreOpfReaderTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "sw-calibre-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }

        GC.SuppressFinalize(this);
    }

    private string WriteBookWithOpf(string opfXml, byte[]? coverBytes = null)
    {
        string bookPath = Path.Combine(tempDir, "book.epub");
        File.WriteAllText(bookPath, "not a real epub");
        File.WriteAllText(Path.Combine(tempDir, ICalibreOpfReader.OpfFileName), opfXml);
        if (coverBytes is not null)
        {
            File.WriteAllBytes(Path.Combine(tempDir, "cover.jpg"), coverBytes);
        }

        return bookPath;
    }

    [Fact]
    public async Task TryReadAsync_returns_null_when_no_sidecar_present()
    {
        string bookPath = Path.Combine(tempDir, "lonely.epub");
        File.WriteAllText(bookPath, "x");

        var result = await reader.TryReadAsync(bookPath);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryReadAsync_parses_core_fields_authors_and_tags()
    {
        const string opf = """
            <?xml version='1.0' encoding='utf-8'?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="uuid_id" version="2.0">
                <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                    <dc:title>The Night Land</dc:title>
                    <dc:creator opf:file-as="Hodgson, William Hope" opf:role="aut">William Hope Hodgson</dc:creator>
                    <dc:creator opf:role="edt">Some Editor</dc:creator>
                    <dc:description>A classic of weird fiction.</dc:description>
                    <dc:publisher>E-BOOKARAMA</dc:publisher>
                    <dc:date>2019-01-03T00:00:00+00:00</dc:date>
                    <dc:language>eng</dc:language>
                    <dc:subject>Horror</dc:subject>
                    <dc:subject>Weird Fiction</dc:subject>
                    <dc:identifier opf:scheme="ISBN">9781234567890</dc:identifier>
                    <meta name="calibre:series" content="Night Cycle"/>
                    <meta name="calibre:series_index" content="2.5"/>
                </metadata>
                <guide>
                    <reference type="cover" title="Cover" href="cover.jpg"/>
                </guide>
            </package>
            """;

        string bookPath = WriteBookWithOpf(opf, coverBytes: [1, 2, 3, 4]);

        var result = await reader.TryReadAsync(bookPath);

        Assert.NotNull(result);
        Assert.Equal("The Night Land", result!.Title);
        Assert.Equal("A classic of weird fiction.", result.Description);
        Assert.Equal("E-BOOKARAMA", result.Publisher);
        Assert.Equal("eng", result.Language);
        Assert.Equal("9781234567890", result.Isbn);
        Assert.Equal(2019, result.PublishedOn!.Value.Year);

        // Only opf:role="aut" creators count as authors.
        Assert.Equal(["William Hope Hodgson"], result.AuthorNames);
        Assert.Equal(["Horror", "Weird Fiction"], result.Tags);

        Assert.Equal("Night Cycle", result.SeriesName);
        Assert.Equal(2.5m, result.NumberInSeries);

        Assert.NotNull(result.Cover);
        Assert.Equal("jpg", result.Cover!.Extension);
        Assert.Equal([1, 2, 3, 4], result.Cover.Data);
    }

    [Fact]
    public async Task TryReadAsync_treats_creator_without_role_as_author()
    {
        const string opf = """
            <?xml version='1.0' encoding='utf-8'?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
                <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                    <dc:title>Mumbai Singularity</dc:title>
                    <dc:creator>Nym Coy</dc:creator>
                </metadata>
            </package>
            """;

        string bookPath = WriteBookWithOpf(opf);

        var result = await reader.TryReadAsync(bookPath);

        Assert.NotNull(result);
        Assert.Equal(["Nym Coy"], result!.AuthorNames);
        Assert.Null(result.Cover);
    }
}
