using System.Text;
using System.Xml;

namespace Quillwright.Xml;

/// <summary>
/// Capture and replay of markup the model does not interpret. Attributes are kept as decoded
/// name/value pairs (re-escaped on the way out); elements are kept as the verbatim XML the
/// reader produced, so anything from an OMML equation to a SmartArt drawing survives a round trip.
/// </summary>
internal static class RawXml
{
    /// <summary>Attributes of the current element, excluding the given names. Returns <see langword="null"/> when nothing is left.</summary>
    public static List<KeyValuePair<string, string>>? ReadAttributes(XmlReader xml, params ReadOnlySpan<string> skip)
    {
        if (!xml.HasAttributes)
            return null;

        List<KeyValuePair<string, string>>? attributes = null;
        if (xml.MoveToFirstAttribute())
        {
            do
            {
                if (skip.Contains(xml.Name))
                    continue;
                (attributes ??= []).Add(new KeyValuePair<string, string>(xml.Name, xml.Value));
            }
            while (xml.MoveToNextAttribute());

            xml.MoveToElement();
        }

        return attributes;
    }

    /// <summary>Writes captured attributes, each preceded by a space, ready to sit inside a start tag.</summary>
    public static void WriteAttributes(Utf8XmlWriter writer, List<KeyValuePair<string, string>>? attributes)
    {
        if (attributes is null)
            return;

        foreach ((string name, string value) in attributes)
        {
            writer.WriteRaw(" "u8);
            writer.WriteRawXml(name);
            writer.WriteRaw("=\""u8);
            writer.WriteAttributeText(value);
            writer.WriteRaw("\""u8);
        }
    }

    /// <summary>Writes a captured element fragment verbatim.</summary>
    public static void Write(Utf8XmlWriter writer, string? fragment)
    {
        if (!string.IsNullOrEmpty(fragment))
            writer.WriteRawXml(fragment);
    }

    /// <summary>Writes every fragment of a captured slot in the order it was captured.</summary>
    public static void WriteAll(Utf8XmlWriter writer, List<string>? fragments)
    {
        if (fragments is null)
            return;

        foreach (string fragment in fragments)
            writer.WriteRawXml(fragment);
    }

    /// <summary>Appends the current element's markup to a buffer, leaving the reader on the following node.</summary>
    public static StringBuilder AppendOuterXml(StringBuilder? buffer, XmlReader xml)
    {
        buffer ??= new StringBuilder();
        buffer.Append(xml.ReadOuterXml());
        return buffer;
    }

    /// <summary>
    /// Collects the current element's markup into a list slot, leaving the reader on the
    /// following node. Repeated captures keep document order within the slot.
    /// </summary>
    public static void Collect(ref List<string>? slot, XmlReader xml) =>
        (slot ??= []).Add(xml.ReadOuterXml());
}
