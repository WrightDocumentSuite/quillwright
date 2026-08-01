using System.Xml;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>Reads the styles part (<c>styles.xml</c>).</summary>
internal static class StylesPartReader
{
    /// <summary>Reads the whole part into a style sheet.</summary>
    public static StyleSheet Read(XmlReader xml, LoadContext context)
    {
        var sheet = new StyleSheet { DefaultRunFormat = RunFormat.Default };
        MoveToRoot(xml, "styles");
        sheet.Attributes = XmlHelp.CaptureRootAttributes(xml);

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "docDefaults":
                    ReadDefaults(reader, sheet, context);
                    return;
                case "latentStyles":
                    sheet.LatentStylesXml = reader.ReadOuterXml();
                    return;
                case "style":
                    sheet.Add(ReadStyle(reader, context));
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

        return sheet;
    }

    /// <summary>Positions the reader on the root element of a part.</summary>
    public static void MoveToRoot(XmlReader xml, string localName)
    {
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == localName)
                return;
        }
    }

    private static void ReadDefaults(XmlReader xml, StyleSheet sheet, LoadContext context) =>
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "rPrDefault":
                    XmlHelp.ForEachChild(reader, (inner, child) =>
                    {
                        if (child == "rPr")
                            sheet.DefaultRunFormat = context.Intern(RunFormatReader.Read(inner));
                        else
                            inner.Skip();
                    });

                    return;
                case "pPrDefault":
                    XmlHelp.ForEachChild(reader, (inner, child) =>
                    {
                        if (child == "pPr")
                            sheet.DefaultParagraphFormat = context.Intern(ParagraphFormatReader.Read(inner).Format);
                        else
                            inner.Skip();
                    });

                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

    private static Style ReadStyle(XmlReader xml, LoadContext context)
    {
        var style = new Style(
            XmlHelp.Attr(xml, "styleId") ?? "Unnamed",
            OoxmlEnums.ParseStyleKind(XmlHelp.Attr(xml, "type")))
        {
            IsDefault = XmlHelp.AttrBool(xml, "default") ?? false,
            IsCustom = XmlHelp.AttrBool(xml, "customStyle") ?? false,
            Attributes = XmlHelp.CaptureAttributes(xml, "styleId", "type", "default", "customStyle"),
        };

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "name": style.Name = XmlHelp.Val(reader); reader.Skip(); return;
                case "aliases": style.Aliases = XmlHelp.Val(reader); reader.Skip(); return;
                case "basedOn": style.BasedOn = XmlHelp.Val(reader); reader.Skip(); return;
                case "next": style.NextStyle = XmlHelp.Val(reader); reader.Skip(); return;
                case "link": style.LinkedStyle = XmlHelp.Val(reader); reader.Skip(); return;
                case "uiPriority": style.Priority = XmlHelp.ValInt(reader); reader.Skip(); return;
                case "semiHidden": style.SemiHidden = XmlHelp.Toggle(reader); reader.Skip(); return;
                case "unhideWhenUsed": style.UnhideWhenUsed = XmlHelp.Toggle(reader); reader.Skip(); return;
                case "qFormat": style.QuickFormat = XmlHelp.Toggle(reader); reader.Skip(); return;
                case "locked": style.Locked = XmlHelp.Toggle(reader); reader.Skip(); return;
                case "autoRedefine": style.AutoRedefine = XmlHelp.Toggle(reader); reader.Skip(); return;
                case "hidden": style.Hidden = XmlHelp.Toggle(reader); reader.Skip(); return;
                case "personal": style.Personal = XmlHelp.Toggle(reader); reader.Skip(); return;
                case "personalCompose": style.PersonalCompose = XmlHelp.Toggle(reader); reader.Skip(); return;
                case "personalReply": style.PersonalReply = XmlHelp.Toggle(reader); reader.Skip(); return;
                case "rsid": style.RsidXml = reader.ReadOuterXml(); return;
                case "pPr":
                    ParagraphProperties properties = ParagraphFormatReader.Read(reader);
                    style.ParagraphFormat = context.Intern(properties.Format);
                    style.NumberingId ??= properties.Format.NumberingId;
                    return;
                case "rPr":
                    style.RunFormat = context.Intern(RunFormatReader.Read(reader));
                    return;
                case "tblPr":
                    style.TableFormat = TableFormatReader.ReadTable(reader);
                    return;
                case "trPr":
                    style.RowFormat = TableFormatReader.ReadRow(reader);
                    return;
                case "tcPr":
                    style.CellFormat = TableFormatReader.ReadCell(reader);
                    return;
                case "tblStylePr":
                    if (ReadConditional(reader, context) is { } conditional)
                        style.ConditionalFormats.Add(conditional);
                    return;
                default:
                    style.Extensions = (style.Extensions ?? string.Empty) + reader.ReadOuterXml();
                    return;
            }
        });

        return style;
    }

    private static ConditionalTableStyle? ReadConditional(XmlReader xml, LoadContext context)
    {
        if (OoxmlEnums.ParseRegion(XmlHelp.Attr(xml, "type")) is not { } region)
        {
            xml.Skip();
            return null;
        }

        var conditional = new ConditionalTableStyle { Region = region };
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "pPr":
                    conditional.ParagraphFormat = context.Intern(ParagraphFormatReader.Read(reader).Format);
                    return;
                case "rPr":
                    conditional.RunFormat = context.Intern(RunFormatReader.Read(reader));
                    return;
                case "tblPr":
                    conditional.TableFormat = TableFormatReader.ReadTable(reader);
                    return;
                case "trPr":
                    conditional.RowFormat = TableFormatReader.ReadRow(reader);
                    return;
                case "tcPr":
                    conditional.CellFormat = TableFormatReader.ReadCell(reader);
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

        return conditional;
    }
}
