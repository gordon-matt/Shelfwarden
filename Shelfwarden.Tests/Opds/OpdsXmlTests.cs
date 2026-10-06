using System.Xml.Linq;
using Shelfwarden.Models.Opds;
using Shelfwarden.Services.Opds;

namespace Shelfwarden.Tests.Opds;

/// <summary>
/// The serialised documents are what reading apps parse, often with strict XML parsers that reject
/// the whole feed over one bad character or a missing namespace.
/// </summary>
public class OpdsXmlTests
{
    private static readonly XNamespace Atom = OpdsNamespaces.Atom;
    private static readonly XNamespace Dc = OpdsNamespaces.DublinCore;
    private static readonly XNamespace OpenSearch = OpdsNamespaces.OpenSearch;

    [Fact]
    public void Feed_is_utf8_atom_with_the_opds_namespaces_declared()
    {
        string xml = OpdsXmlSerializer.Serialize(SampleFeed());

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml, StringComparison.OrdinalIgnoreCase);

        var root = XDocument.Parse(xml).Root!;
        Assert.Equal(Atom + "feed", root.Name);
        Assert.Equal(OpdsNamespaces.DublinCore, root.GetNamespaceOfPrefix("dc")?.NamespaceName);
        Assert.Equal(OpdsNamespaces.Opds, root.GetNamespaceOfPrefix("opds")?.NamespaceName);
        Assert.Equal(OpdsNamespaces.OpenSearch, root.GetNamespaceOfPrefix("opensearch")?.NamespaceName);
    }

    [Fact]
    public void Timestamps_are_rfc3339_utc()
    {
        var feed = SampleFeed();
        feed.Updated = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        feed.Entries[0].Updated = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

        var root = XDocument.Parse(OpdsXmlSerializer.Serialize(feed)).Root!;

        Assert.Equal("2026-03-04T05:06:07Z", root.Element(Atom + "updated")!.Value);
        Assert.Equal("2026-01-02T03:04:05Z", root.Element(Atom + "entry")!.Element(Atom + "updated")!.Value);
    }

    [Fact]
    public void Local_times_are_converted_to_utc()
    {
        var local = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), OpdsDates.Format(local));
    }

    [Fact]
    public void Dublin_core_fields_use_the_dc_namespace_and_are_omitted_when_empty()
    {
        var feed = SampleFeed();
        var entry = feed.Entries[0];
        entry.Language = "en";
        entry.Publisher = "Tor";
        entry.Issued = "2001-02-03";

        var bare = new OpdsEntry { Id = "urn:test:bare", Title = "Bare", Updated = DateTime.UtcNow };
        feed.Entries.Add(bare);

        var entries = XDocument.Parse(OpdsXmlSerializer.Serialize(feed)).Root!.Elements(Atom + "entry").ToList();

        Assert.Equal("en", entries[0].Element(Dc + "language")?.Value);
        Assert.Equal("Tor", entries[0].Element(Dc + "publisher")?.Value);
        Assert.Equal("2001-02-03", entries[0].Element(Dc + "issued")?.Value);

        Assert.Null(entries[1].Element(Dc + "language"));
        Assert.Null(entries[1].Element(Dc + "publisher"));
        Assert.Null(entries[1].Element(Atom + "summary"));
        Assert.Null(entries[1].Element(Atom + "author"));
    }

    [Fact]
    public void Opensearch_paging_elements_appear_only_when_set()
    {
        var feed = SampleFeed();
        var root = XDocument.Parse(OpdsXmlSerializer.Serialize(feed)).Root!;
        Assert.Null(root.Element(OpenSearch + "totalResults"));

        feed.TotalResults = 120;
        feed.ItemsPerPage = 50;
        feed.StartIndex = 51;
        root = XDocument.Parse(OpdsXmlSerializer.Serialize(feed)).Root!;

        Assert.Equal("120", root.Element(OpenSearch + "totalResults")?.Value);
        Assert.Equal("50", root.Element(OpenSearch + "itemsPerPage")?.Value);
        Assert.Equal("51", root.Element(OpenSearch + "startIndex")?.Value);
    }

    [Fact]
    public void Markup_characters_in_metadata_are_escaped()
    {
        var feed = SampleFeed();
        feed.Entries[0].Title = "Fish & Chips <deluxe> \"edition\"";

        var root = XDocument.Parse(OpdsXmlSerializer.Serialize(feed)).Root!;

        Assert.Equal("Fish & Chips <deluxe> \"edition\"", root.Element(Atom + "entry")!.Element(Atom + "title")!.Value);
    }

    [Fact]
    public void Link_length_is_written_only_when_known()
    {
        var feed = SampleFeed();
        feed.Entries[0].Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Acquisition, Href = "/a", Type = OpdsMediaTypes.Epub, Length = 1234 });
        feed.Entries[0].Links.Add(new OpdsLink { Rel = OpdsLinkRelations.Image, Href = "/b", Type = "image/png" });

        var links = XDocument.Parse(OpdsXmlSerializer.Serialize(feed)).Root!
            .Element(Atom + "entry")!.Elements(Atom + "link").ToList();

        Assert.Equal("1234", links[0].Attribute("length")?.Value);
        Assert.Null(links[1].Attribute("length"));
    }

    [Fact]
    public void Feed_media_type_carries_the_opds_kind()
    {
        Assert.Equal("application/atom+xml;profile=opds-catalog;kind=navigation", new OpdsFeed { Kind = OpdsFeedKind.Navigation }.MediaType);
        Assert.Equal("application/atom+xml;profile=opds-catalog;kind=acquisition", new OpdsFeed { Kind = OpdsFeedKind.Acquisition }.MediaType);
    }

    [Fact]
    public void Opensearch_description_declares_its_namespace_and_template()
    {
        var description = new OpenSearchDescription
        {
            ShortName = "Shelfwarden",
            Description = "Search",
            Urls = [new OpenSearchUrl { Type = OpdsMediaTypes.AcquisitionFeed, Template = "/opds/search?q={searchTerms}" }],
        };

        var root = XDocument.Parse(OpdsXmlSerializer.Serialize(description)).Root!;

        Assert.Equal(OpenSearch + "OpenSearchDescription", root.Name);
        var url = Assert.Single(root.Elements(OpenSearch + "Url"));
        Assert.Equal("/opds/search?q={searchTerms}", url.Attribute("template")?.Value);
        Assert.Equal(OpdsMediaTypes.AcquisitionFeed, url.Attribute("type")?.Value);
    }

    [Theory]
    [InlineData("Plain title", "Plain title")]
    [InlineData("Nul\0inside", "Nulinside")]
    [InlineData("Bell\u0007 and form\u000C feed", "Bell and form feed")]
    [InlineData("Tabs\tand\nnewlines\rstay", "Tabs\tand\nnewlines\rstay")]
    [InlineData("Non-characters \uFFFE\uFFFF gone", "Non-characters  gone")]
    public void Sanitiser_removes_only_characters_xml_cannot_hold(string input, string expected)
    {
        Assert.Equal(expected, OpdsTextSanitizer.Clean(input));
    }

    // Not an InlineData case: xUnit's theory serialisation turns lone surrogates into U+FFFD.
    [Fact]
    public void Sanitiser_drops_unpaired_surrogates()
    {
        Assert.Equal("Unpaired  surrogate", OpdsTextSanitizer.Clean("Unpaired \uD800 surrogate"));
        Assert.Equal("Trailing ", OpdsTextSanitizer.Clean("Trailing \uD83D"));
    }

    [Fact]
    public void Sanitiser_keeps_emoji_and_non_latin_text()
    {
        const string text = "Dragons 🐉 und Märchen — 三体";
        Assert.Equal(text, OpdsTextSanitizer.Clean(text));
    }

    [Fact]
    public void Sanitised_control_characters_produce_a_parseable_feed()
    {
        var feed = SampleFeed();
        feed.Entries[0].Title = OpdsTextSanitizer.Clean("Broken\0Title\u0001 🐉");

        var root = XDocument.Parse(OpdsXmlSerializer.Serialize(feed)).Root!;

        Assert.Equal("BrokenTitle 🐉", root.Element(Atom + "entry")!.Element(Atom + "title")!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \0  ")]
    public void CleanOrNull_treats_blank_as_missing(string? input)
    {
        Assert.Null(OpdsTextSanitizer.CleanOrNull(input));
    }

    [Fact]
    public void Html_descriptions_become_plain_paragraphs()
    {
        string? text = OpdsTextSanitizer.HtmlToPlainText("<p>First <b>bold</b> line.</p><p>Second &amp; last.</p>");
        Assert.Equal("First bold line.\n\nSecond & last.", text);
    }

    private static OpdsFeed SampleFeed() => new()
    {
        Kind = OpdsFeedKind.Acquisition,
        Id = "urn:test:feed",
        Title = "Test",
        Updated = DateTime.UtcNow,
        Entries = [new OpdsEntry { Id = "urn:test:entry", Title = "Entry", Updated = DateTime.UtcNow }],
    };
}
