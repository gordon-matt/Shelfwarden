using System.Text;
using System.Xml.Serialization;
using Extenso;
using Shelfwarden.Models.Opds;

namespace Shelfwarden.Services.Opds;

/// <summary>
/// Writes OPDS 1.2 (Atom) documents. Strings must already be XML-safe — see
/// <see cref="OpdsTextSanitizer.Clean"/> — since the writer rejects invalid characters rather than dropping them.
/// </summary>
public static class OpdsXmlSerializer
{
    private static readonly XmlSerializerNamespaces FeedNamespaces = CreateFeedNamespaces();

    private static readonly XmlSerializerNamespaces OpenSearchNamespaces = CreateOpenSearchNamespaces();

    public static string Serialize(OpdsFeed feed) =>
        feed.XmlSerialize(omitXmlDeclaration: false, xmlns: FeedNamespaces, encoding: Encoding.UTF8);

    public static string Serialize(OpenSearchDescription description) =>
        description.XmlSerialize(omitXmlDeclaration: false, xmlns: OpenSearchNamespaces, encoding: Encoding.UTF8);

    private static XmlSerializerNamespaces CreateFeedNamespaces()
    {
        var ns = new XmlSerializerNamespaces();
        ns.Add(string.Empty, OpdsNamespaces.Atom);
        ns.Add("dc", OpdsNamespaces.DublinCore);
        ns.Add("opds", OpdsNamespaces.Opds);
        ns.Add("opensearch", OpdsNamespaces.OpenSearch);
        return ns;
    }

    private static XmlSerializerNamespaces CreateOpenSearchNamespaces()
    {
        var ns = new XmlSerializerNamespaces();
        ns.Add(string.Empty, OpdsNamespaces.OpenSearch);
        return ns;
    }
}
