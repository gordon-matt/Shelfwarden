namespace Shelfwarden.Models.Opds;

/// <summary>XML namespaces used by OPDS 1.2 catalogue documents.</summary>
public static class OpdsNamespaces
{
    public const string Atom = "http://www.w3.org/2005/Atom";
    public const string DublinCore = "http://purl.org/dc/terms/";
    public const string Opds = "http://opds-spec.org/2010/catalog";
    public const string OpenSearch = "http://a9.com/-/spec/opensearch/1.1/";
}

/// <summary>Media types for OPDS feeds, OpenSearch descriptors and the files they point at.</summary>
public static class OpdsMediaTypes
{
    public const string NavigationFeed = "application/atom+xml;profile=opds-catalog;kind=navigation";
    public const string AcquisitionFeed = "application/atom+xml;profile=opds-catalog;kind=acquisition";

    /// <summary>Plain Atom, used for the OpenSearch result template (what clients expect back from a search).</summary>
    public const string Atom = "application/atom+xml";

    public const string OpenSearchDescription = "application/opensearchdescription+xml";

    public const string Epub = "application/epub+zip";
    public const string Pdf = "application/pdf";
    public const string OctetStream = "application/octet-stream";

    public static string ForFormat(EbookFormat format) => format switch
    {
        EbookFormat.Epub => Epub,
        EbookFormat.Pdf => Pdf,
        _ => OctetStream,
    };
}

/// <summary>Atom / OPDS link relations.</summary>
public static class OpdsLinkRelations
{
    public const string Self = "self";
    public const string Start = "start";
    public const string Up = "up";
    public const string Search = "search";
    public const string Subsection = "subsection";
    public const string Related = "related";

    public const string First = "first";
    public const string Previous = "previous";
    public const string Next = "next";
    public const string Last = "last";

    public const string Acquisition = "http://opds-spec.org/acquisition";
    public const string Image = "http://opds-spec.org/image";
    public const string Thumbnail = "http://opds-spec.org/image/thumbnail";
    public const string SortNew = "http://opds-spec.org/sort/new";
}
