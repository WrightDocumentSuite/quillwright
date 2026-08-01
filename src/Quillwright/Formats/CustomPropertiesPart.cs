using System.Globalization;
using System.Xml;
using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Reads and writes the custom properties part (<c>docProps/custom.xml</c>, ISO/IEC 29500-1
/// §22.3), whose values are the variant types of §22.4.
/// </summary>
/// <remarks>
/// Each property is numbered from 2 upwards in document order. The numbering is not free:
/// zero and one are reserved for the dictionary and the code page of the OLE property set the
/// part corresponds to, and Word renumbers the rest on every save, so writing them in sequence
/// is both simpler and closer to what a consumer expects than carrying the original numbers.
/// </remarks>
internal static class CustomPropertiesPart
{
    private const int FirstPropertyId = 2;

    /// <summary>Reads the whole part.</summary>
    public static void Read(XmlReader xml, CustomPropertyCollection properties)
    {
        StylesPartReader.MoveToRoot(xml, "Properties");
        if (xml.NodeType != XmlNodeType.Element)
            return;

        properties.Clear();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name != "property")
            {
                reader.Skip();
                return;
            }

            if (ReadProperty(reader) is { } property)
                properties.Add(property);
        });
    }

    /// <summary>Writes the whole part.</summary>
    public static void Write(Utf8XmlWriter writer, CustomPropertyCollection properties)
    {
        writer.WriteDeclaration();
        writer.WriteRaw("<Properties xmlns=\""u8);
        writer.WriteRawXml(DocxSchema.NsCustomProperties);
        writer.WriteRaw("\" xmlns:vt=\""u8);
        writer.WriteRawXml(DocxSchema.NsVariantTypes);
        writer.WriteRaw("\">"u8);

        int id = FirstPropertyId;
        foreach (CustomProperty property in properties)
        {
            writer.WriteRaw("<property fmtid=\""u8);
            writer.WriteAttributeText(CustomPropertyCollection.FormatId);
            writer.WriteRaw("\" pid=\""u8);
            writer.WriteRawXml((id++).ToString(CultureInfo.InvariantCulture));
            writer.WriteRaw("\" name=\""u8);
            writer.WriteAttributeText(property.Name);
            if (property.LinkTarget is { } target)
            {
                writer.WriteRaw("\" linkTarget=\""u8);
                writer.WriteAttributeText(target);
            }

            writer.WriteRaw("\">"u8);
            WriteValue(writer, property.Value);
            writer.WriteRaw("</property>"u8);
        }

        writer.WriteRaw("</Properties>"u8);
    }

    private static CustomProperty? ReadProperty(XmlReader xml)
    {
        string? name = xml.GetAttribute("name");
        string? linkTarget = xml.GetAttribute("linkTarget");
        var value = default(PropertyValue);

        XmlHelp.ForEachChild(xml, (reader, type) =>
        {
            PropertyValue parsed = ParseValue(type, reader.ReadElementContentAsString());
            if (!parsed.IsEmpty)
                value = parsed;
        });

        return string.IsNullOrEmpty(name) ? null : new CustomProperty(name, value) { LinkTarget = linkTarget };
    }

    /// <summary>Turns one variant element into a value, keeping the text of anything unmodelled.</summary>
    private static PropertyValue ParseValue(string type, string text) => type switch
    {
        "lpwstr" or "lpstr" or "bstr" => PropertyValue.FromText(text),
        "i1" or "i2" or "i4" or "i8" or "int" or "ui1" or "ui2" or "ui4" or "ui8" or "uint" =>
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
                ? PropertyValue.FromInteger(number)
                : PropertyValue.FromText(text),
        "r4" or "r8" or "decimal" =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)
                ? PropertyValue.FromReal(real)
                : PropertyValue.FromText(text),
        "bool" => XmlHelp.ParseOnOff(text) is { } flag ? PropertyValue.FromBoolean(flag) : PropertyValue.FromText(text),
        "filetime" or "date" =>
            XmlHelp.ParseDate(text) is { } moment ? PropertyValue.FromDateTime(moment) : PropertyValue.FromText(text),
        "clsid" => PropertyValue.FromGuid(text),
        "empty" or "null" => default,
        _ => PropertyValue.FromText(text),
    };

    private static void WriteValue(Utf8XmlWriter writer, PropertyValue value)
    {
        ReadOnlySpan<byte> element = value.Kind switch
        {
            PropertyValueKind.Integer => "vt:i4"u8,
            PropertyValueKind.Real => "vt:r8"u8,
            PropertyValueKind.Boolean => "vt:bool"u8,
            PropertyValueKind.DateTime => "vt:filetime"u8,
            PropertyValueKind.Guid => "vt:clsid"u8,
            PropertyValueKind.Empty => "vt:lpwstr"u8,
            _ => "vt:lpwstr"u8,
        };

        writer.WriteRaw("<"u8);
        writer.WriteRaw(element);
        writer.WriteRaw(">"u8);
        writer.WriteText(value.ToString());
        writer.WriteRaw("</"u8);
        writer.WriteRaw(element);
        writer.WriteRaw(">"u8);
    }
}
