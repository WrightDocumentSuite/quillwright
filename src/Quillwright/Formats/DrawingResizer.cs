using System.Globalization;
using System.Text;
using System.Xml;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// Changes the size, the name and the image of a drawing without rebuilding it.
/// </summary>
/// <remarks>
/// Generating a drawing from the model produces the plainest possible one: a picture in the
/// text flow with no effects, no cropping and no wrapping. That is fine for a picture the
/// caller just created, and quietly destructive for one read from a file — resizing a floating
/// picture would drop the anchor that made it float. Rewriting the attributes that changed
/// leaves everything else exactly as it was.
/// </remarks>
internal static class DrawingResizer
{
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    /// <summary>
    /// Rewrites a drawing to match a picture, or returns <see langword="null"/> when the
    /// markup cannot be read and the caller should generate a new drawing instead.
    /// </summary>
    /// <param name="markup">The original <c>w:drawing</c>.</param>
    /// <param name="picture">The picture as the model now has it.</param>
    /// <param name="relationshipId">Relationship id of the image part.</param>
    public static string? Rewrite(string markup, Picture picture, string? relationshipId)
    {
        var output = new StringBuilder(markup.Length);
        var settings = new XmlWriterSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            OmitXmlDeclaration = true,
            Indent = false,
            CheckCharacters = false,
        };

        try
        {
            using var reader = XmlReader.Create(new StringReader(markup), Xml.XmlDefaults.ReaderSettings);
            using var writer = XmlWriter.Create(output, settings);
            Copy(reader, writer, picture, relationshipId);
        }
        catch (XmlException)
        {
            return null;
        }

        return output.Length == 0 ? null : output.ToString();
    }

    private static void Copy(XmlReader reader, XmlWriter writer, Picture picture, string? relationshipId)
    {
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                    bool empty = reader.IsEmptyElement;
                    CopyAttributes(reader, writer, picture, relationshipId);
                    if (empty)
                        writer.WriteEndElement();
                    break;
                case XmlNodeType.EndElement:
                    writer.WriteEndElement();
                    break;
                case XmlNodeType.Text:
                case XmlNodeType.SignificantWhitespace:
                case XmlNodeType.Whitespace:
                    writer.WriteString(reader.Value);
                    break;
                case XmlNodeType.CDATA:
                    writer.WriteCData(reader.Value);
                    break;
            }
        }
    }

    private static void CopyAttributes(XmlReader reader, XmlWriter writer, Picture picture, string? relationshipId)
    {
        string element = reader.LocalName;
        string ns = reader.NamespaceURI;
        bool named = false;
        bool described = false;
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                // Namespace declarations are left to the writer, which puts each prefix back
                // at its first use; writing them again would declare the same prefix twice.
                if (reader.NamespaceURI == XmlnsNamespace || reader.Prefix == "xmlns" || reader.Name == "xmlns")
                    continue;

                named |= reader.LocalName == "name";
                described |= reader.LocalName == "descr";
                string value = Replacement(element, ns, reader, picture, relationshipId) ?? reader.Value;
                writer.WriteAttributeString(reader.Prefix, reader.LocalName, reader.NamespaceURI, value);
            }
            while (reader.MoveToNextAttribute());

            reader.MoveToElement();
        }

        // A name or an alternative text the file never had has to be added rather than
        // replaced, which is the one thing rewriting in place cannot do by copying.
        if (element is not ("docPr" or "cNvPr"))
            return;
        if (!named && picture.Name is { } name)
            writer.WriteAttributeString("name", name);
        if (!described && picture.Description is { } description)
            writer.WriteAttributeString("descr", description);
    }

    /// <summary>The new value of an attribute the model owns, or nothing to keep what was there.</summary>
    private static string? Replacement(
        string element, string ns, XmlReader attribute, Picture picture, string? relationshipId) =>
        (element, attribute.LocalName) switch
        {
            // The drawing's own extent and the picture's inside it both carry the size.
            ("extent", "cx") when ns == DocxSchema.NsWordDrawing => Emu(picture.Width),
            ("extent", "cy") when ns == DocxSchema.NsWordDrawing => Emu(picture.Height),
            ("ext", "cx") when ns == DocxSchema.NsDrawing => Emu(picture.Width),
            ("ext", "cy") when ns == DocxSchema.NsDrawing => Emu(picture.Height),
            ("docPr" or "cNvPr", "name") when picture.Name is { } name => name,
            ("docPr" or "cNvPr", "descr") when picture.Description is { } description => description,
            ("blip", "embed") when ns == DocxSchema.NsDrawing && relationshipId is not null => relationshipId,
            _ => null,
        };

    private static string Emu(Primitives.Length value) =>
        Math.Min(value.Emu, int.MaxValue).ToString(CultureInfo.InvariantCulture);
}
