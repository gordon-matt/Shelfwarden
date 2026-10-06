using Shelfwarden.Services;
using Shelfwarden.Services.Opds;

namespace Shelfwarden.Tests.Opds;

/// <summary>
/// Feed links are host-relative so they survive reverse proxies; these pin the shapes clients
/// see, including sub-path hosting and the key-in-URL form.
/// </summary>
public class OpdsLinkBuilderTests
{
    [Fact]
    public void Links_are_host_relative_and_page_one_has_no_query()
    {
        var links = new OpdsLinkBuilder();

        Assert.Equal("/opds", links.Root());
        Assert.Equal("/opds/recent", links.Recent());
        Assert.Equal("/opds/recent?page=3", links.Recent(3));
        Assert.Equal("/opds/authors/7?page=2", links.Author(7, 2));
        Assert.Equal("/opds/books/5/cover", links.Cover(5));
        Assert.Equal("/opds/opensearch.xml", links.OpenSearchDescription());
    }

    [Theory]
    [InlineData("/shelfwarden")]
    [InlineData("/shelfwarden/")]
    public void Path_base_prefixes_every_link(string pathBase)
    {
        var links = new OpdsLinkBuilder(pathBase);

        Assert.Equal("/shelfwarden/opds", links.Root());
        Assert.Equal("/shelfwarden/opds/series/4", links.Series(4));
        Assert.Equal("/shelfwarden/favicon.ico", links.Icon);
    }

    [Fact]
    public void Key_in_url_clients_keep_the_key_on_every_link()
    {
        var links = new OpdsLinkBuilder("/base", "abc123");

        Assert.Equal("/base/opds/key/abc123", links.Root());
        Assert.Equal("/base/opds/key/abc123/books/9/thumbnail", links.Thumbnail(9));
        Assert.Equal("/base/opds/key/abc123/search?q={searchTerms}", links.SearchTemplate());
    }

    [Fact]
    public void Search_terms_and_file_names_are_escaped()
    {
        var links = new OpdsLinkBuilder();

        Assert.Equal("/opds/search?q=fish%20%26%20chips&page=2", links.Search("fish & chips", 2));
        Assert.Equal("/opds/books/1/download/Les%20Mis%C3%A9rables%3F.epub", links.Download(1, "Les Misérables?.epub"));
    }

    [Theory]
    [InlineData("Les Misérables", @"C:\b\x.epub", EbookFormat.Epub, "Les Misérables.epub")]
    [InlineData("What? A: Story/Part \"1\"", "/b/x.epub", EbookFormat.Epub, "What A StoryPart 1.epub")]
    [InlineData("Trailing dots...", "/b/x.pdf", EbookFormat.Pdf, "Trailing dots.pdf")]
    [InlineData("三体", "/b/x", EbookFormat.Epub, "三体.epub")]
    [InlineData("???", "/b/x.pdf", EbookFormat.Pdf, "book-42.pdf")]
    [InlineData(null, "/books/original-name.epub", EbookFormat.Epub, "original-name.epub")]
    public void Download_file_names_are_safe_on_any_filesystem(string? title, string path, EbookFormat format, string expected)
    {
        Assert.Equal(expected, BookFileNames.BuildDownloadFileName(42, title, path, format));
    }
}
