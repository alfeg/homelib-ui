using System.Text;
using System.Xml;

namespace MyHomeLib.Web.Services.Opds;

public record OpdsLink(string Rel, string Type, string Href, string? Title = null);

public record OpdsEntry(
    string Id,
    string Title,
    string? Content,
    IReadOnlyList<OpdsLink> Links)
{
    public IReadOnlyList<string> Authors { get; init; } = [];
    public IReadOnlyList<(string Term, string Label)> Categories { get; init; } = [];
    public string? Language { get; init; }
    public DateTimeOffset? Updated { get; init; }
}

/// <summary>
/// Writes OPDS 1.2 (Atom) catalog feeds.
/// </summary>
/// <remarks>
/// Hand-written rather than generated from a model graph: e-reader OPDS clients are strict
/// about element order, namespaces and the exact link type strings, and the payload has to
/// stay small enough for a device with a few megabytes of parser budget.
/// </remarks>
public sealed class OpdsFeed
{
    public const string NavigationType = "application/atom+xml;profile=opds-catalog;kind=navigation";
    public const string AcquisitionType = "application/atom+xml;profile=opds-catalog;kind=acquisition";
    public const string OpenSearchType = "application/opensearchdescription+xml";

    private const string AtomNs = "http://www.w3.org/2005/Atom";
    private const string OpdsNs = "http://opds-spec.org/2010/catalog";
    private const string DcNs = "http://purl.org/dc/terms/";

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string SelfHref { get; init; }
    public required bool IsAcquisition { get; init; }
    public string? Subtitle { get; init; }
    public string? UpHref { get; init; }
    public string? NextHref { get; init; }
    public string? PreviousHref { get; init; }
    public DateTimeOffset Updated { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<OpdsEntry> Entries { get; init; } = [];

    /// <summary>
    /// Writes through a UTF-8-declaring writer. XmlWriter takes the encoding for its
    /// declaration from the underlying TextWriter, and a plain StringWriter reports UTF-16
    /// — which produces a feed whose declaration contradicts its Content-Type header and
    /// which strict e-reader clients reject.
    /// </summary>
    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    public string ToXml()
    {
        var output = new StringBuilder(Entries.Count * 400 + 512);
        using var textWriter = new Utf8StringWriter(output);
        var settings = new XmlWriterSettings
        {
            Indent = false,
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(false),
            NewLineHandling = NewLineHandling.Replace,
        };

        using (var writer = XmlWriter.Create(textWriter, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("feed", AtomNs);
            writer.WriteAttributeString("xmlns", "opds", null, OpdsNs);
            writer.WriteAttributeString("xmlns", "dc", null, DcNs);

            writer.WriteElementString("id", AtomNs, Id);
            writer.WriteElementString("title", AtomNs, Title);
            if (!string.IsNullOrWhiteSpace(Subtitle))
                writer.WriteElementString("subtitle", AtomNs, Subtitle);
            writer.WriteElementString("updated", AtomNs, Updated.ToString("yyyy-MM-ddTHH:mm:ssZ"));

            var selfType = IsAcquisition ? AcquisitionType : NavigationType;
            WriteLink(writer, new OpdsLink("self", selfType, SelfHref));
            WriteLink(writer, new OpdsLink("start", NavigationType, "/opds"));
            WriteLink(writer, new OpdsLink("search", OpenSearchType, "/opds/opensearch.xml"));

            if (UpHref is not null) WriteLink(writer, new OpdsLink("up", NavigationType, UpHref));
            if (NextHref is not null) WriteLink(writer, new OpdsLink("next", selfType, NextHref));
            if (PreviousHref is not null) WriteLink(writer, new OpdsLink("previous", selfType, PreviousHref));

            foreach (var entry in Entries) WriteEntry(writer, entry);

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return output.ToString();
    }

    private void WriteEntry(XmlWriter writer, OpdsEntry entry)
    {
        writer.WriteStartElement("entry", AtomNs);

        writer.WriteElementString("id", AtomNs, entry.Id);
        writer.WriteElementString("title", AtomNs, entry.Title);
        writer.WriteElementString("updated", AtomNs, (entry.Updated ?? Updated).ToString("yyyy-MM-ddTHH:mm:ssZ"));

        foreach (var author in entry.Authors)
        {
            writer.WriteStartElement("author", AtomNs);
            writer.WriteElementString("name", AtomNs, author);
            writer.WriteEndElement();
        }

        if (!string.IsNullOrWhiteSpace(entry.Language))
            writer.WriteElementString("language", DcNs, entry.Language);

        foreach (var (term, label) in entry.Categories)
        {
            writer.WriteStartElement("category", AtomNs);
            writer.WriteAttributeString("term", term);
            if (!string.IsNullOrWhiteSpace(label)) writer.WriteAttributeString("label", label);
            writer.WriteEndElement();
        }

        if (!string.IsNullOrWhiteSpace(entry.Content))
        {
            writer.WriteStartElement("content", AtomNs);
            writer.WriteAttributeString("type", "text");
            writer.WriteString(entry.Content);
            writer.WriteEndElement();
        }

        foreach (var link in entry.Links) WriteLink(writer, link);

        writer.WriteEndElement();
    }

    private static void WriteLink(XmlWriter writer, OpdsLink link)
    {
        writer.WriteStartElement("link", AtomNs);
        writer.WriteAttributeString("rel", link.Rel);
        writer.WriteAttributeString("type", link.Type);
        writer.WriteAttributeString("href", link.Href);
        if (!string.IsNullOrWhiteSpace(link.Title)) writer.WriteAttributeString("title", link.Title);
        writer.WriteEndElement();
    }
}
