using System.Text;
using System.Xml;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>Reads block-level content — paragraphs, tables and block content controls — into a container.</summary>
internal sealed partial class BodyReader
{
    private readonly LoadContext _context;

    /// <summary>Creates a reader bound to a load in progress.</summary>
    public BodyReader(LoadContext context) => _context = context;

    /// <summary>Reads the children of a container element such as <c>w:body</c> or <c>w:tc</c>.</summary>
    public void ReadBlocks(XmlReader xml, BlockContainer container) =>
        XmlHelp.ForEachChild(xml, (reader, name) => ReadBlock(reader, name, container));

    /// <summary>Reads one block element, consuming it, and returns it without attaching it anywhere.</summary>
    public Block? ReadBlockElement(XmlReader xml, string name) => name switch
    {
        "p" => ReadParagraph(xml),
        "tbl" => ReadTable(xml),
        "sdt" => ReadBlockContentControl(xml),
        "AlternateContent" when xml.NamespaceURI == DocxSchema.NsMarkupCompatibility => ReadAlternateBlock(xml),
        _ => new RawBlock(xml.ReadOuterXml()),
    };

    private void ReadBlock(XmlReader xml, string name, BlockContainer container)
    {
        switch (name)
        {
            case "p":
                container.Blocks.Add(ReadParagraph(xml));
                return;
            case "tbl":
                container.Blocks.Add(ReadTable(xml));
                return;
            case "sdt":
                container.Blocks.Add(ReadBlockContentControl(xml));
                return;
            case "AlternateContent" when xml.NamespaceURI == DocxSchema.NsMarkupCompatibility:
                container.Blocks.Add(ReadAlternateBlock(xml));
                return;
            case "sectPr":
                // Handled by the caller, which owns the section split.
                xml.Skip();
                return;
            default:
                container.Blocks.Add(new RawBlock(xml.ReadOuterXml()));
                return;
        }
    }

    /// <summary>Reads a <c>w:p</c> element, consuming it.</summary>
    public Paragraph ReadParagraph(XmlReader xml)
    {
        var paragraph = new Paragraph { Attributes = XmlHelp.CaptureAttributes(xml) };
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name == "pPr")
            {
                ParagraphProperties properties = ParagraphFormatReader.Read(reader);
                paragraph.Format = properties.Format;
                paragraph.MarkFormat = properties.MarkFormat;
                paragraph.SectionBreak = properties.Section;
                return;
            }

            ReadInline(reader, name, paragraph);
        });

        return paragraph;
    }

    private BlockContentControl ReadBlockContentControl(XmlReader xml)
    {
        var control = new BlockContentControl();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "sdtPr":
                    control.PropertiesXml = reader.ReadOuterXml();
                    ReadControlProperties(control.PropertiesXml, out string? tag, out string? alias, out int? id);
                    (control.Tag, control.Alias, control.Id) = (tag, alias, id);
                    return;
                case "sdtEndPr":
                    control.EndPropertiesXml = reader.ReadOuterXml();
                    return;
                case "sdtContent":
                    ReadBlocks(reader, control.Content);
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

        return control;
    }

    /// <summary>Reads a <c>w:tbl</c> element, consuming it.</summary>
    public Table ReadTable(XmlReader xml)
    {
        var table = new Table { Attributes = XmlHelp.CaptureAttributes(xml) };
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "tblPr":
                    table.Format = TableFormatReader.ReadTable(reader);
                    return;
                case "tblGrid":
                    ReadGrid(reader, table);
                    return;
                case "tr":
                    table.Rows.Add(ReadRow(reader));
                    return;
                default:
                    // A bookmark spanning rows, or a tag wrapped around them: not modelled,
                    // but dropping it would lose a reference the rest of the file still makes.
                    table.PreservedXml += reader.ReadOuterXml();
                    return;
            }
        });

        return table;
    }

    private static void ReadGrid(XmlReader xml, Table table) =>
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name == "gridCol")
                table.Grid.Add(XmlHelp.AttrTwips(reader, "w") ?? Length.Zero);
            else if (name == "tblGridChange")
            {
                table.GridChangeXml = reader.ReadOuterXml();
                return;
            }

            reader.Skip();
        });

    private TableRow ReadRow(XmlReader xml)
    {
        var row = new TableRow { Attributes = XmlHelp.CaptureAttributes(xml) };
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "tblPrEx":
                    row.PropertyExceptionsXml = reader.ReadOuterXml();
                    return;
                case "trPr":
                    row.Format = TableFormatReader.ReadRow(reader);
                    return;
                case "tc":
                    row.Cells.Add(ReadCell(reader));
                    return;
                default:
                    row.PreservedXml += reader.ReadOuterXml();
                    return;
            }
        });

        return row;
    }

    private TableCell ReadCell(XmlReader xml)
    {
        var cell = new TableCell { Attributes = XmlHelp.CaptureAttributes(xml) };
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name == "tcPr")
            {
                cell.Format = TableFormatReader.ReadCell(reader);
                return;
            }

            ReadBlock(reader, name, cell);
        });

        return cell;
    }

    private static void ReadControlProperties(string propertiesXml, out string? tag, out string? alias, out int? id)
    {
        tag = null;
        alias = null;
        id = null;

        using var reader = XmlReader.Create(new StringReader(propertiesXml), Xml.XmlDefaults.ReaderSettings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            switch (reader.LocalName)
            {
                case "tag":
                    tag = XmlHelp.Val(reader);
                    break;
                case "alias":
                    alias = XmlHelp.Val(reader);
                    break;
                case "id":
                    id ??= XmlHelp.ValInt(reader);
                    break;
            }
        }
    }

    private static string BuildStartTag(XmlReader xml)
    {
        var builder = new StringBuilder("<").Append(xml.Name);
        if (xml.HasAttributes && xml.MoveToFirstAttribute())
        {
            do
            {
                builder.Append(' ').Append(xml.Name).Append("=\"")
                    .Append(System.Security.SecurityElement.Escape(xml.Value)).Append('"');
            }
            while (xml.MoveToNextAttribute());

            xml.MoveToElement();
        }

        return builder.Append('>').ToString();
    }
}
