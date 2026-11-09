using System.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Reads back the properties a revision record preserved. A record made in this session uses the
/// <c>w:</c> prefix without declaring it, so the prefix is supplied here; one read from a file
/// carries its own declarations, which take precedence.
/// </summary>
internal static class RecordedMarkup
{
    /// <summary>Finds the named element inside a record and reads it.</summary>
    /// <param name="recordXml">The record, such as a <c>w:rPrChange</c>.</param>
    /// <param name="childName">Local name of the element holding the old properties.</param>
    /// <param name="read">Reads that element, consuming it.</param>
    /// <returns>What was read, or <see langword="default"/> when the record has no such element.</returns>
    public static T? ReadChild<T>(string recordXml, string childName, Func<XmlReader, T> read)
        where T : class
    {
        var names = new NameTable();
        var namespaces = new XmlNamespaceManager(names);
        namespaces.AddNamespace("w", DocxSchema.NsWord);

        var settings = new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            NameTable = names,
        };

        using var text = new StringReader(recordXml);
        using XmlReader xml = XmlReader.Create(text, settings, new XmlParserContext(names, namespaces, null, XmlSpace.None));
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == childName)
                return read(xml);
        }

        return null;
    }
}
