using System.Globalization;
using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>Writes the core properties part (<c>docProps/core.xml</c>).</summary>
internal static class CorePropertiesWriter
{
    private const string DublinCore = "http://purl.org/dc/elements/1.1/";
    private const string DublinTerms = "http://purl.org/dc/terms/";

    /// <summary>Writes the whole part.</summary>
    public static void Write(Utf8XmlWriter writer, DocumentProperties properties, SaveOptions options)
    {
        writer.WriteDeclaration();
        writer.WriteRaw(
            "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\""u8 +
            " xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\""u8 +
            " xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"u8);

        Element(writer, "dc:title"u8, properties.Title);
        Element(writer, "dc:subject"u8, properties.Subject);
        Element(writer, "dc:creator"u8, properties.Creator);
        Element(writer, "cp:keywords"u8, properties.Keywords);
        Element(writer, "dc:description"u8, properties.Description);
        Element(writer, "cp:lastModifiedBy"u8, properties.LastModifiedBy);
        Element(writer, "cp:revision"u8, properties.Revision);
        Timestamp(writer, "dcterms:created"u8, properties.Created ?? DateTimeOffset.Now);
        Timestamp(writer, "dcterms:modified"u8,
            options.UpdateModifiedTimestamp ? DateTimeOffset.Now : properties.Modified ?? DateTimeOffset.Now);
        Element(writer, "cp:category"u8, properties.Category);
        Element(writer, "cp:contentStatus"u8, properties.ContentStatus);
        Element(writer, "dc:language"u8, properties.Language);

        writer.WriteRaw("</cp:coreProperties>"u8);
    }

    /// <summary>The Dublin Core namespace, exposed so the reader recognises the same vocabulary.</summary>
    public static bool IsDublinCore(string? uri) => uri is DublinCore or DublinTerms;

    private static void Element(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, string? value)
    {
        if (value is null)
            return;

        writer.WriteRaw("<"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(">"u8);
        writer.WriteText(value);
        writer.WriteRaw("</"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(">"u8);
    }

    private static void Timestamp(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, DateTimeOffset value)
    {
        writer.WriteRaw("<"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(" xsi:type=\"dcterms:W3CDTF\">"u8);
        writer.WriteRawXml(value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        writer.WriteRaw("</"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(">"u8);
    }
}
