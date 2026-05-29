using Shelfwarden.Services.Tts;

namespace Shelfwarden.Tests;

public class EpubHtmlTextExtractionTests
{
    [Fact]
    public void ExtractParagraphs_strips_head_title_text()
    {
        const string html = """
            <?xml version='1.0' encoding='utf-8'?>
            <html>
              <head>
                <title>c1X</title>
                <meta charset="utf-8"/>
              </head>
              <body>
                <h1>VERIPHYSICS</h1>
                <p>Real content here.</p>
              </body>
            </html>
            """;

        var paragraphs = EpubHtmlTextExtractor.ExtractParagraphs(html).ToList();

        Assert.Contains("VERIPHYSICS", paragraphs);
        Assert.Contains("Real content here.", paragraphs);
        Assert.DoesNotContain(paragraphs, p => p.Contains("c1X", StringComparison.OrdinalIgnoreCase));
    }
}
