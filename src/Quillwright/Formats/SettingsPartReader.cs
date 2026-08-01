using System.Xml;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>Reads the settings part (<c>settings.xml</c>) as its elements in document order.</summary>
internal static class SettingsPartReader
{
    /// <summary>Reads the whole part.</summary>
    public static void Read(XmlReader xml, DocumentSettings settings)
    {
        StylesPartReader.MoveToRoot(xml, "settings");
        if (xml.NodeType != XmlNodeType.Element)
            return;

        settings.Attributes = XmlHelp.CaptureRootAttributes(xml);
        settings.Clear();
        XmlHelp.ForEachChild(xml, (reader, name) => settings.Append(name, reader.ReadOuterXml()));
    }
}

/// <summary>Reads the core properties part (<c>docProps/core.xml</c>).</summary>
internal static class CorePropertiesReader
{
    /// <summary>Reads the whole part.</summary>
    public static DocumentProperties Read(XmlReader xml)
    {
        var properties = new DocumentProperties();
        StylesPartReader.MoveToRoot(xml, "coreProperties");
        if (xml.NodeType != XmlNodeType.Element)
            return properties;

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            string value = reader.ReadElementContentAsString();
            switch (name)
            {
                case "title": properties.Title = value; return;
                case "subject": properties.Subject = value; return;
                case "creator": properties.Creator = value; return;
                case "keywords": properties.Keywords = value; return;
                case "description": properties.Description = value; return;
                case "lastModifiedBy": properties.LastModifiedBy = value; return;
                case "revision": properties.Revision = value; return;
                case "created": properties.Created = XmlHelp.ParseDate(value); return;
                case "modified": properties.Modified = XmlHelp.ParseDate(value); return;
                case "category": properties.Category = value; return;
                case "contentStatus": properties.ContentStatus = value; return;
                case "language": properties.Language = value; return;
                default: return;
            }
        });

        return properties;
    }
}
