using System.Xml;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>Everything a <c>w:pPr</c> can carry beyond the paragraph format itself.</summary>
internal readonly record struct ParagraphProperties(
    ParagraphFormat Format,
    RunFormat MarkFormat,
    SectionProperties? Section);

/// <summary>Reads paragraph formatting (<c>w:pPr</c>).</summary>
internal static class ParagraphFormatReader
{
    /// <summary>Reads a <c>w:pPr</c> element, consuming it.</summary>
    public static ParagraphProperties Read(XmlReader xml)
    {
        var format = ParagraphFormat.Default;
        var markFormat = RunFormat.Default;
        SectionProperties? section = null;

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "rPr":
                    markFormat = RunFormatReader.Read(reader);
                    return;
                case "sectPr":
                    section = SectionReader.Read(reader);
                    return;
                default:
                    format = ReadChild(reader, name, format);
                    return;
            }
        });

        return new ParagraphProperties(format, markFormat, section);
    }

    /// <summary>Reads one child of <c>w:pPr</c>, consuming it.</summary>
    public static ParagraphFormat ReadChild(XmlReader xml, string name, ParagraphFormat format)
    {
        switch (name)
        {
            case "pStyle": return Consume(xml, format with { StyleId = XmlHelp.Val(xml) });
            case "keepNext": return Consume(xml, format with { KeepWithNext = XmlHelp.Toggle(xml) });
            case "keepLines": return Consume(xml, format with { KeepLinesTogether = XmlHelp.Toggle(xml) });
            case "pageBreakBefore": return Consume(xml, format with { PageBreakBefore = XmlHelp.Toggle(xml) });
            case "widowControl": return Consume(xml, format with { WidowControl = XmlHelp.Toggle(xml) });
            case "numPr": return ReadNumbering(xml, format);
            case "suppressLineNumbers": return Consume(xml, format with { SuppressLineNumbers = XmlHelp.Toggle(xml) });
            case "pBdr": return format with { Borders = SharedFormatReader.ReadBorderSet(xml) };
            case "shd": return format with { Shading = SharedFormatReader.ReadShading(xml) };
            case "tabs": return format with { Tabs = SharedFormatReader.ReadTabs(xml) };
            case "suppressAutoHyphens": return Consume(xml, format with { SuppressAutoHyphens = XmlHelp.Toggle(xml) });
            case "kinsoku": return Consume(xml, format with { Kinsoku = XmlHelp.Toggle(xml) });
            case "wordWrap": return Consume(xml, format with { WordWrap = XmlHelp.Toggle(xml) });
            case "overflowPunct": return Consume(xml, format with { OverflowPunctuation = XmlHelp.Toggle(xml) });
            case "topLinePunct": return Consume(xml, format with { TopLinePunctuation = XmlHelp.Toggle(xml) });
            case "autoSpaceDE": return Consume(xml, format with { AutoSpaceEastAsianLatin = XmlHelp.Toggle(xml) });
            case "autoSpaceDN": return Consume(xml, format with { AutoSpaceEastAsianNumbers = XmlHelp.Toggle(xml) });
            case "bidi": return Consume(xml, format with { RightToLeft = XmlHelp.Toggle(xml) });
            case "adjustRightInd": return Consume(xml, format with { AdjustRightIndent = XmlHelp.Toggle(xml) });
            case "snapToGrid": return Consume(xml, format with { SnapToGrid = XmlHelp.Toggle(xml) });
            case "spacing": return ReadSpacing(xml, format);
            case "ind": return ReadIndent(xml, format);
            case "contextualSpacing": return Consume(xml, format with { ContextualSpacing = XmlHelp.Toggle(xml) });
            case "mirrorIndents": return Consume(xml, format with { MirrorIndents = XmlHelp.Toggle(xml) });
            case "suppressOverlap": return Consume(xml, format with { SuppressOverlap = XmlHelp.Toggle(xml) });
            case "jc": return Consume(xml, format with { Alignment = OoxmlEnums.ParseAlignment(XmlHelp.Val(xml)) });
            case "textDirection": return Consume(xml, format with { TextDirection = OoxmlEnums.ParseTextDirection(XmlHelp.Val(xml)) });
            case "textAlignment": return Consume(xml, format with { LineTextAlignment = OoxmlEnums.ParseLineTextAlignment(XmlHelp.Val(xml)) });
            case "outlineLvl": return Consume(xml, format with { OutlineLevel = XmlHelp.ValInt(xml) });

            case "framePr": return format with { FrameXml = xml.ReadOuterXml() };
            case "textboxTightWrap": return format with { TextboxTightWrapXml = xml.ReadOuterXml() };
            case "divId": return format with { DivIdXml = xml.ReadOuterXml() };
            case "cnfStyle": return format with { ConditionalFormattingXml = xml.ReadOuterXml() };
            case "pPrChange": return format with { ChangeXml = xml.ReadOuterXml() };
            default: return format with { Extensions = (format.Extensions ?? string.Empty) + xml.ReadOuterXml() };
        }
    }

    private static ParagraphFormat Consume(XmlReader xml, ParagraphFormat format)
    {
        xml.Skip();
        return format;
    }

    private static ParagraphFormat ReadNumbering(XmlReader xml, ParagraphFormat format)
    {
        int? level = null;
        int? id = null;
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name == "ilvl")
                level = XmlHelp.ValInt(reader);
            else if (name == "numId")
                id = XmlHelp.ValInt(reader);
            reader.Skip();
        });

        return format with { NumberingLevel = level, NumberingId = id };
    }

    private static ParagraphFormat ReadSpacing(XmlReader xml, ParagraphFormat format)
    {
        ParagraphFormat result = format with
        {
            SpacingBefore = XmlHelp.AttrTwips(xml, "before"),
            SpacingAfter = XmlHelp.AttrTwips(xml, "after"),
            SpacingBeforeLines = XmlHelp.AttrInt(xml, "beforeLines"),
            SpacingAfterLines = XmlHelp.AttrInt(xml, "afterLines"),
            SpacingBeforeAuto = XmlHelp.AttrBool(xml, "beforeAutospacing"),
            SpacingAfterAuto = XmlHelp.AttrBool(xml, "afterAutospacing"),
            LineSpacing = XmlHelp.AttrTwips(xml, "line"),
            LineSpacingRule = OoxmlEnums.ParseLineRule(XmlHelp.Attr(xml, "lineRule")),
        };

        xml.Skip();
        return result;
    }

    private static ParagraphFormat ReadIndent(XmlReader xml, ParagraphFormat format)
    {
        ParagraphFormat result = format with
        {
            IndentLeft = XmlHelp.AttrTwips(xml, "left") ?? XmlHelp.AttrTwips(xml, "start"),
            IndentRight = XmlHelp.AttrTwips(xml, "right") ?? XmlHelp.AttrTwips(xml, "end"),
            IndentFirstLine = XmlHelp.AttrTwips(xml, "firstLine"),
            IndentHanging = XmlHelp.AttrTwips(xml, "hanging"),
            IndentLeftCharacters = XmlHelp.AttrInt(xml, "leftChars") ?? XmlHelp.AttrInt(xml, "startChars"),
            IndentRightCharacters = XmlHelp.AttrInt(xml, "rightChars") ?? XmlHelp.AttrInt(xml, "endChars"),
            IndentFirstLineCharacters = XmlHelp.AttrInt(xml, "firstLineChars"),
            IndentHangingCharacters = XmlHelp.AttrInt(xml, "hangingChars"),
        };

        xml.Skip();
        return result;
    }
}
