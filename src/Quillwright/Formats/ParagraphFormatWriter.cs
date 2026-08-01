using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Writes paragraph formatting (<c>w:pPr</c>) in the order <c>CT_PPr</c> declares.
/// </summary>
internal static class ParagraphFormatWriter
{
    /// <summary>
    /// Writes a complete <c>w:pPr</c> including the paragraph mark's run properties and, for
    /// the paragraph that ends a section, the section properties.
    /// </summary>
    /// <param name="writer">Destination.</param>
    /// <param name="format">Paragraph formatting.</param>
    /// <param name="markFormat">Formatting of the paragraph mark.</param>
    /// <param name="section">Section properties to embed, or <see langword="null"/>.</param>
    /// <param name="context">Supplies header and footer relationship ids for the section.</param>
    public static void Write(
        Utf8XmlWriter writer,
        ParagraphFormat format,
        RunFormat markFormat,
        SectionProperties? section,
        SectionWriteContext? context)
    {
        bool hasMark = !markFormat.IsEmpty;
        if (format.IsEmpty && !hasMark && section is null)
            return;

        writer.WriteRaw("<w:pPr>"u8);
        WriteBody(writer, format);
        RunFormatWriter.Write(writer, hasMark ? markFormat : null);
        if (section is not null && context is not null)
            SectionWriter.Write(writer, section, context);
        RawXml.Write(writer, format.ChangeXml);
        writer.WriteRaw("</w:pPr>"u8);
    }

    /// <summary>Writes the children of <c>w:pPr</c> that come from the format itself.</summary>
    public static void WriteBody(Utf8XmlWriter writer, ParagraphFormat format)
    {
        WordXml.Value(writer, "pStyle"u8, format.StyleId);
        WordXml.Toggle(writer, "keepNext"u8, format.KeepWithNext);
        WordXml.Toggle(writer, "keepLines"u8, format.KeepLinesTogether);
        WordXml.Toggle(writer, "pageBreakBefore"u8, format.PageBreakBefore);
        RawXml.Write(writer, format.FrameXml);
        WordXml.Toggle(writer, "widowControl"u8, format.WidowControl);
        WriteNumbering(writer, format);
        WordXml.Toggle(writer, "suppressLineNumbers"u8, format.SuppressLineNumbers);
        SharedFormatWriter.ParagraphBorders(writer, format.Borders);
        SharedFormatWriter.Shading(writer, format.Shading);
        WriteTabs(writer, format);
        WordXml.Toggle(writer, "suppressAutoHyphens"u8, format.SuppressAutoHyphens);
        WordXml.Toggle(writer, "kinsoku"u8, format.Kinsoku);
        WordXml.Toggle(writer, "wordWrap"u8, format.WordWrap);
        WordXml.Toggle(writer, "overflowPunct"u8, format.OverflowPunctuation);
        WordXml.Toggle(writer, "topLinePunct"u8, format.TopLinePunctuation);
        WordXml.Toggle(writer, "autoSpaceDE"u8, format.AutoSpaceEastAsianLatin);
        WordXml.Toggle(writer, "autoSpaceDN"u8, format.AutoSpaceEastAsianNumbers);
        WordXml.Toggle(writer, "bidi"u8, format.RightToLeft);
        WordXml.Toggle(writer, "adjustRightInd"u8, format.AdjustRightIndent);
        WordXml.Toggle(writer, "snapToGrid"u8, format.SnapToGrid);
        WriteSpacing(writer, format);
        WriteIndent(writer, format);
        WordXml.Toggle(writer, "contextualSpacing"u8, format.ContextualSpacing);
        WordXml.Toggle(writer, "mirrorIndents"u8, format.MirrorIndents);
        WordXml.Toggle(writer, "suppressOverlap"u8, format.SuppressOverlap);
        if (format.Alignment is { } alignment)
            WordXml.Value(writer, "jc"u8, OoxmlEnums.Name(alignment, writer.Strict));
        if (format.TextDirection is { } direction)
            WordXml.Value(writer, "textDirection"u8, OoxmlEnums.Name(direction));
        if (format.LineTextAlignment is { } lineAlignment)
            WordXml.Value(writer, "textAlignment"u8, OoxmlEnums.Name(lineAlignment));
        RawXml.Write(writer, format.TextboxTightWrapXml);
        WordXml.Value(writer, "outlineLvl"u8, format.OutlineLevel);
        RawXml.Write(writer, format.DivIdXml);
        RawXml.Write(writer, format.ConditionalFormattingXml);
        RawXml.Write(writer, format.Extensions);
    }

    private static void WriteNumbering(Utf8XmlWriter writer, ParagraphFormat format)
    {
        if (format.NumberingId is null && format.NumberingLevel is null)
            return;

        writer.WriteRaw("<w:numPr>"u8);
        WordXml.Value(writer, "ilvl"u8, format.NumberingLevel);
        WordXml.Value(writer, "numId"u8, format.NumberingId);
        writer.WriteRaw("</w:numPr>"u8);
    }

    private static void WriteTabs(Utf8XmlWriter writer, ParagraphFormat format)
    {
        if (format.Tabs.IsEmpty)
            return;

        writer.WriteRaw("<w:tabs>"u8);
        foreach (TabStop tab in format.Tabs)
        {
            writer.WriteRaw("<w:tab w:val=\""u8);
            writer.WriteAttributeText(OoxmlEnums.Name(tab.Alignment, writer.Strict));
            writer.WriteRaw("\""u8);
            if (tab.Leader != TabLeader.None)
                WordXml.Attribute(writer, "w:leader"u8, OoxmlEnums.Name(tab.Leader));
            WordXml.AttributeTwips(writer, "w:pos"u8, tab.Position);
            writer.WriteRaw("/>"u8);
        }

        writer.WriteRaw("</w:tabs>"u8);
    }

    private static void WriteSpacing(Utf8XmlWriter writer, ParagraphFormat format)
    {
        if (format is
            {
                SpacingBefore: null, SpacingAfter: null, SpacingBeforeAuto: null, SpacingAfterAuto: null,
                SpacingBeforeLines: null, SpacingAfterLines: null, LineSpacing: null, LineSpacingRule: null,
            })
        {
            return;
        }

        writer.WriteRaw("<w:spacing"u8);
        WordXml.AttributeTwips(writer, "w:before"u8, format.SpacingBefore);
        WordXml.Attribute(writer, "w:beforeLines"u8, format.SpacingBeforeLines);
        WordXml.Attribute(writer, "w:beforeAutospacing"u8, format.SpacingBeforeAuto);
        WordXml.AttributeTwips(writer, "w:after"u8, format.SpacingAfter);
        WordXml.Attribute(writer, "w:afterLines"u8, format.SpacingAfterLines);
        WordXml.Attribute(writer, "w:afterAutospacing"u8, format.SpacingAfterAuto);
        WordXml.AttributeTwips(writer, "w:line"u8, format.LineSpacing);
        if (format.LineSpacingRule is { } rule)
            WordXml.Attribute(writer, "w:lineRule"u8, OoxmlEnums.Name(rule));
        writer.WriteRaw("/>"u8);
    }

    private static void WriteIndent(Utf8XmlWriter writer, ParagraphFormat format)
    {
        if (format is
            {
                IndentLeft: null, IndentRight: null, IndentFirstLine: null, IndentHanging: null,
                IndentLeftCharacters: null, IndentRightCharacters: null,
                IndentFirstLineCharacters: null, IndentHangingCharacters: null,
            })
        {
            return;
        }

        // CT_Ind is one of the types Strict renamed: start/startChars and end/endChars.
        writer.WriteRaw("<w:ind"u8);
        WordXml.AttributeTwips(writer, writer.Strict ? "w:start"u8 : "w:left"u8, format.IndentLeft);
        WordXml.Attribute(writer, writer.Strict ? "w:startChars"u8 : "w:leftChars"u8, format.IndentLeftCharacters);
        WordXml.AttributeTwips(writer, writer.Strict ? "w:end"u8 : "w:right"u8, format.IndentRight);
        WordXml.Attribute(writer, writer.Strict ? "w:endChars"u8 : "w:rightChars"u8, format.IndentRightCharacters);
        WordXml.AttributeTwips(writer, "w:hanging"u8, format.IndentHanging);
        WordXml.Attribute(writer, "w:hangingChars"u8, format.IndentHangingCharacters);
        WordXml.AttributeTwips(writer, "w:firstLine"u8, format.IndentFirstLine);
        WordXml.Attribute(writer, "w:firstLineChars"u8, format.IndentFirstLineCharacters);
        writer.WriteRaw("/>"u8);
    }
}
