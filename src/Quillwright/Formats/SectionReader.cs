using System.Xml;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>Reads section properties (<c>w:sectPr</c>) together with the header and footer references.</summary>
internal static class SectionReader
{
    /// <summary>References a section makes to header and footer parts.</summary>
    public readonly record struct Reference(bool IsFooter, HeaderFooterKind Kind, string RelationshipId);

    /// <summary>Reads a <c>w:sectPr</c> element, consuming it.</summary>
    public static SectionProperties Read(XmlReader xml) => Read(xml, out _);

    /// <summary>Reads a <c>w:sectPr</c> element and collects its header and footer references.</summary>
    public static SectionProperties Read(XmlReader xml, out List<Reference> references)
    {
        var properties = new SectionProperties
        {
            Attributes = XmlHelp.CaptureAttributes(xml),
            Columns = new ColumnLayout(),
        };

        var collected = new List<Reference>();
        XmlHelp.ForEachChild(xml, (reader, name) => ReadChild(reader, name, properties, collected));
        references = collected;
        properties.LoadedReferences = collected;
        return properties;
    }

    private static void ReadChild(XmlReader xml, string name, SectionProperties properties, List<Reference> references)
    {
        switch (name)
        {
            case "headerReference":
            case "footerReference":
                if (XmlHelp.RelAttr(xml) is { } relationshipId)
                {
                    references.Add(new Reference(
                        name[0] == 'f',
                        XmlHelp.Attr(xml, "type") switch
                        {
                            "first" => HeaderFooterKind.First,
                            "even" => HeaderFooterKind.Even,
                            _ => HeaderFooterKind.Default,
                        },
                        relationshipId));
                }

                xml.Skip();
                return;

            case "type":
                properties.Start = OoxmlEnums.ParseSectionStart(XmlHelp.Val(xml));
                xml.Skip();
                return;

            case "pgSz":
                properties.PageWidth = XmlHelp.AttrTwips(xml, "w") ?? properties.PageWidth;
                properties.PageHeight = XmlHelp.AttrTwips(xml, "h") ?? properties.PageHeight;
                properties.Orientation = XmlHelp.Attr(xml, "orient") == "landscape"
                    ? PageOrientation.Landscape
                    : PageOrientation.Portrait;
                properties.PaperCode = XmlHelp.AttrInt(xml, "code");
                xml.Skip();
                return;

            case "pgMar":
                ReadMargins(xml, properties.Margins);
                xml.Skip();
                return;

            case "pgBorders":
                properties.PageBordersAttributes = XmlHelp.CaptureAttributes(xml);
                properties.PageBorders = SharedFormatReader.ReadBorderSet(xml);
                return;

            case "pgNumType":
                ReadPageNumbering(xml, properties.PageNumbering);
                xml.Skip();
                return;

            case "cols":
                ReadColumns(xml, properties.Columns);
                return;

            case "formProt":
                properties.FormProtection = XmlHelp.Toggle(xml);
                xml.Skip();
                return;

            case "vAlign":
                properties.VerticalAlignment = OoxmlEnums.ParseCellAlign(XmlHelp.Val(xml));
                xml.Skip();
                return;

            case "noEndnote":
                properties.SuppressEndnotes = XmlHelp.Toggle(xml);
                xml.Skip();
                return;

            case "titlePg":
                properties.DifferentFirstPage = XmlHelp.Toggle(xml);
                xml.Skip();
                return;

            case "textDirection":
                properties.TextDirection = OoxmlEnums.ParseTextDirection(XmlHelp.Val(xml));
                xml.Skip();
                return;

            case "bidi":
                properties.RightToLeft = XmlHelp.Toggle(xml);
                xml.Skip();
                return;

            case "rtlGutter":
                properties.RightToLeftGutter = XmlHelp.Toggle(xml);
                xml.Skip();
                return;

            case "footnotePr":
                properties.FootnotePropertiesXml = xml.ReadOuterXml();
                return;
            case "endnotePr":
                properties.EndnotePropertiesXml = xml.ReadOuterXml();
                return;
            case "paperSrc":
                properties.PaperSourceXml = xml.ReadOuterXml();
                return;
            case "lnNumType":
                properties.LineNumberingXml = xml.ReadOuterXml();
                return;
            case "docGrid":
                properties.DocumentGridXml = xml.ReadOuterXml();
                return;
            case "printerSettings":
                properties.PrinterSettingsXml = xml.ReadOuterXml();
                return;
            case "sectPrChange":
                properties.ChangeXml = xml.ReadOuterXml();
                return;
            default:
                properties.Extensions = (properties.Extensions ?? string.Empty) + xml.ReadOuterXml();
                return;
        }
    }

    private static void ReadMargins(XmlReader xml, PageMargins margins)
    {
        margins.Top = XmlHelp.AttrTwips(xml, "top") ?? margins.Top;
        margins.Bottom = XmlHelp.AttrTwips(xml, "bottom") ?? margins.Bottom;
        margins.Left = XmlHelp.AttrTwips(xml, "left") ?? XmlHelp.AttrTwips(xml, "start") ?? margins.Left;
        margins.Right = XmlHelp.AttrTwips(xml, "right") ?? XmlHelp.AttrTwips(xml, "end") ?? margins.Right;
        margins.Header = XmlHelp.AttrTwips(xml, "header") ?? margins.Header;
        margins.Footer = XmlHelp.AttrTwips(xml, "footer") ?? margins.Footer;
        margins.Gutter = XmlHelp.AttrTwips(xml, "gutter") ?? margins.Gutter;
    }

    private static void ReadPageNumbering(XmlReader xml, PageNumbering numbering)
    {
        if (XmlHelp.Attr(xml, "fmt") is { } format)
            (numbering.Format, numbering.CustomFormat) = OoxmlEnums.ParseNumberFormat(format);
        numbering.Start = XmlHelp.AttrInt(xml, "start");
        numbering.ChapterStyleLevel = XmlHelp.AttrInt(xml, "chapStyle");
        numbering.ChapterSeparator = XmlHelp.Attr(xml, "chapSep");
    }

    private static void ReadColumns(XmlReader xml, ColumnLayout columns)
    {
        columns.Count = XmlHelp.AttrInt(xml, "num") ?? 1;
        columns.Space = XmlHelp.AttrTwips(xml, "space") ?? columns.Space;
        columns.Separator = XmlHelp.AttrBool(xml, "sep") ?? false;
        columns.EqualWidth = XmlHelp.AttrBool(xml, "equalWidth") ?? true;

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name == "col")
            {
                columns.Columns.Add(new TextColumn(
                    XmlHelp.AttrTwips(reader, "w") ?? Length.Zero,
                    XmlHelp.AttrTwips(reader, "space") ?? Length.Zero));
            }

            reader.Skip();
        });

        if (columns.Columns.Count > 0)
            columns.Count = Math.Max(columns.Count, columns.Columns.Count);
    }
}
