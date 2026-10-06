using System.Xml.Serialization;

namespace Shelfwarden.Models.Opds;

/// <summary>
/// An OPDS 1.2 catalogue feed (an Atom <c>feed</c> document). Navigation and acquisition feeds
/// share this shape; <see cref="Kind"/> picks the media type. The XML attributes describe the Atom
/// serialisation — another serialiser (e.g. OPDS 2.0 JSON) can map from the same object.
/// </summary>
[XmlRoot("feed", Namespace = OpdsNamespaces.Atom)]
public class OpdsFeed
{
    [XmlIgnore]
    public OpdsFeedKind Kind { get; set; }

    [XmlElement("id")]
    public string Id { get; set; } = string.Empty;

    [XmlElement("title")]
    public string Title { get; set; } = string.Empty;

    [XmlIgnore]
    public DateTime Updated { get; set; }

    [XmlElement("updated")]
    public string UpdatedText
    {
        get => OpdsDates.Format(Updated);
        set => Updated = DateTime.Parse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
    }

    [XmlElement("icon")]
    public string? Icon { get; set; }

    [XmlElement("author")]
    public OpdsAuthor? Author { get; set; }

    [XmlElement("totalResults", Namespace = OpdsNamespaces.OpenSearch)]
    public int? TotalResults { get; set; }

    [XmlElement("itemsPerPage", Namespace = OpdsNamespaces.OpenSearch)]
    public int? ItemsPerPage { get; set; }

    [XmlElement("startIndex", Namespace = OpdsNamespaces.OpenSearch)]
    public int? StartIndex { get; set; }

    [XmlElement("link")]
    public List<OpdsLink> Links { get; set; } = [];

    [XmlElement("entry")]
    public List<OpdsEntry> Entries { get; set; } = [];

    [XmlIgnore]
    public string MediaType => Kind == OpdsFeedKind.Acquisition
        ? OpdsMediaTypes.AcquisitionFeed
        : OpdsMediaTypes.NavigationFeed;

    public bool ShouldSerializeTotalResults() => TotalResults.HasValue;

    public bool ShouldSerializeItemsPerPage() => ItemsPerPage.HasValue;

    public bool ShouldSerializeStartIndex() => StartIndex.HasValue;
}

public enum OpdsFeedKind
{
    Navigation = 0,
    Acquisition = 1,
}

public class OpdsEntry
{
    [XmlElement("id")]
    public string Id { get; set; } = string.Empty;

    [XmlElement("title")]
    public string Title { get; set; } = string.Empty;

    [XmlIgnore]
    public DateTime Updated { get; set; }

    [XmlElement("updated")]
    public string UpdatedText
    {
        get => OpdsDates.Format(Updated);
        set => Updated = DateTime.Parse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
    }

    [XmlElement("author")]
    public List<OpdsAuthor> Authors { get; set; } = [];

    [XmlElement("language", Namespace = OpdsNamespaces.DublinCore)]
    public string? Language { get; set; }

    [XmlElement("publisher", Namespace = OpdsNamespaces.DublinCore)]
    public string? Publisher { get; set; }

    /// <summary>Publication date as <c>yyyy-MM-dd</c>.</summary>
    [XmlElement("issued", Namespace = OpdsNamespaces.DublinCore)]
    public string? Issued { get; set; }

    [XmlElement("identifier", Namespace = OpdsNamespaces.DublinCore)]
    public List<string> Identifiers { get; set; } = [];

    [XmlElement("category")]
    public List<OpdsCategory> Categories { get; set; } = [];

    /// <summary>Short plain-text description; used for book entries.</summary>
    [XmlElement("summary")]
    public OpdsText? Summary { get; set; }

    /// <summary>Plain-text content; used for navigation entries.</summary>
    [XmlElement("content")]
    public OpdsText? Content { get; set; }

    [XmlElement("link")]
    public List<OpdsLink> Links { get; set; } = [];
}

public class OpdsLink
{
    [XmlAttribute("rel")]
    public string? Rel { get; set; }

    [XmlAttribute("href")]
    public string Href { get; set; } = string.Empty;

    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlAttribute("title")]
    public string? Title { get; set; }

    /// <summary>Size in bytes of the linked resource, when known (acquisition links).</summary>
    [XmlIgnore]
    public long? Length { get; set; }

    [XmlAttribute("length")]
    public long LengthValue
    {
        get => Length ?? 0;
        set => Length = value;
    }

    public bool ShouldSerializeLengthValue() => Length is > 0;
}

public class OpdsAuthor
{
    [XmlElement("name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("uri")]
    public string? Uri { get; set; }
}

public class OpdsCategory
{
    [XmlAttribute("term")]
    public string Term { get; set; } = string.Empty;

    [XmlAttribute("label")]
    public string? Label { get; set; }

    [XmlAttribute("scheme")]
    public string? Scheme { get; set; }
}

/// <summary>Atom text construct (<c>&lt;summary type="text"&gt;…&lt;/summary&gt;</c>).</summary>
public class OpdsText
{
    [XmlAttribute("type")]
    public string Type { get; set; } = "text";

    [XmlText]
    public string Value { get; set; } = string.Empty;
}

/// <summary>OpenSearch 1.1 description document advertised from the root feed.</summary>
[XmlRoot("OpenSearchDescription", Namespace = OpdsNamespaces.OpenSearch)]
public class OpenSearchDescription
{
    [XmlElement("ShortName")]
    public string ShortName { get; set; } = string.Empty;

    [XmlElement("Description")]
    public string Description { get; set; } = string.Empty;

    [XmlElement("InputEncoding")]
    public string InputEncoding { get; set; } = "UTF-8";

    [XmlElement("OutputEncoding")]
    public string OutputEncoding { get; set; } = "UTF-8";

    [XmlElement("Url")]
    public List<OpenSearchUrl> Urls { get; set; } = [];
}

public class OpenSearchUrl
{
    [XmlAttribute("type")]
    public string Type { get; set; } = string.Empty;

    [XmlAttribute("template")]
    public string Template { get; set; } = string.Empty;
}

public static class OpdsDates
{
    /// <summary>RFC 3339 UTC timestamp, second precision (<c>2026-01-31T09:15:00Z</c>).</summary>
    public static string Format(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            // EF hands back stored UtcNow values as Unspecified on most providers.
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value,
        };

        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }
}
