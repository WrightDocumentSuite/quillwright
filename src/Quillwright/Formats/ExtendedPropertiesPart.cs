using System.Xml;
using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Reads and writes the application properties part (<c>docProps/app.xml</c>, ISO/IEC 29500-1
/// §22.2).
/// </summary>
/// <remarks>
/// The part is kept as its elements in document order, so the vectors this version does not
/// model — the heading pairs, the titles of parts, a digital signature — go back exactly as
/// they arrived while the scalar properties around them stay editable.
/// </remarks>
internal static class ExtendedPropertiesPart
{
    /// <summary>Reads the whole part.</summary>
    public static void Read(XmlReader xml, ExtendedProperties properties)
    {
        StylesPartReader.MoveToRoot(xml, "Properties");
        if (xml.NodeType != XmlNodeType.Element)
            return;

        properties.Clear();
        XmlHelp.ForEachChild(xml, (reader, name) => properties.Append(name, reader.ReadOuterXml()));
    }

    /// <summary>Writes the whole part.</summary>
    public static void Write(Utf8XmlWriter writer, ExtendedProperties properties)
    {
        writer.WriteDeclaration();
        writer.WriteRaw("<Properties xmlns=\""u8);
        writer.WriteRawXml(DocxSchema.NsExtendedProperties);
        writer.WriteRaw("\" xmlns:vt=\""u8);
        writer.WriteRawXml(DocxSchema.NsVariantTypes);
        writer.WriteRaw("\">"u8);

        foreach ((_, string xml) in properties.Elements)
            writer.WriteRawXml(xml);

        writer.WriteRaw("</Properties>"u8);
    }
}
