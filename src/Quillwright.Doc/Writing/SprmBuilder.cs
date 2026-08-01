using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Turns the model's format records into the property modifiers a legacy Word file stores
/// them as — the inverse of <see cref="SprmTranslator"/>.
/// </summary>
/// <remarks>
/// The two directions are kept deliberately symmetric: every opcode written here is one the
/// translator reads, and the unit conversions are the same ones inverted. That symmetry is
/// what the builder's tests assert, by building a format, parsing it straight back and
/// comparing the records.
/// </remarks>
internal static class SprmBuilder
{
    /// <summary>Builds the character modifiers for a run.</summary>
    /// <param name="format">The formatting to encode.</param>
    /// <param name="fonts">Resolves a font name to its index in the font table.</param>
    /// <param name="styleIndex">Resolves a character style identifier to its index in the stylesheet.</param>
    public static byte[] BuildRun(RunFormat format, Func<string, int> fonts, Func<string, int>? styleIndex = null)
    {
        var writer = new GrpprlWriter();

        if (format.StyleId is { } styleId && styleIndex?.Invoke(styleId) is > 0 and { } istd)
            writer.UInt16(SprmCode.CharacterStyle, (ushort)istd);

        Toggle(writer, SprmCode.Bold, format.Bold);
        Toggle(writer, SprmCode.Italic, format.Italic);
        Toggle(writer, SprmCode.Strike, format.Strike);
        Toggle(writer, SprmCode.DoubleStrike, format.DoubleStrike);
        Toggle(writer, SprmCode.Outline, format.Outline);
        Toggle(writer, SprmCode.Shadow, format.Shadow);
        Toggle(writer, SprmCode.Emboss, format.Emboss);
        Toggle(writer, SprmCode.Imprint, format.Imprint);
        Toggle(writer, SprmCode.SmallCaps, format.SmallCaps);
        Toggle(writer, SprmCode.Caps, format.Caps);
        Toggle(writer, SprmCode.Hidden, format.Hidden);
        Toggle(writer, SprmCode.WebHidden, format.WebHidden);
        Toggle(writer, SprmCode.NoProof, format.NoProof);
        Toggle(writer, SprmCode.BoldComplexScript, format.BoldComplexScript);
        Toggle(writer, SprmCode.ItalicComplexScript, format.ItalicComplexScript);

        if (format.Underline is { } underline)
            writer.Byte(SprmCode.Underline, UnderlineCode(underline));
        if (format.Size is { } size)
            writer.UInt16(SprmCode.FontSize, (ushort)Math.Clamp(size.HalfPoints, 2, 3276));
        if (format.SizeComplexScript is { } sizeComplex)
            writer.UInt16(SprmCode.FontSizeComplexScript, (ushort)Math.Clamp(sizeComplex.HalfPoints, 2, 3276));
        if (format.Position is { } position)
            writer.Int16(SprmCode.Position, (short)Math.Clamp(position.HalfPoints, short.MinValue, short.MaxValue));
        if (format.Kerning is { } kerning)
            writer.UInt16(SprmCode.Kerning, (ushort)Math.Clamp(kerning.HalfPoints, 0, ushort.MaxValue));
        if (format.CharacterSpacing is { } spacing)
            writer.Int16(SprmCode.CharacterSpacing, (short)Math.Clamp(spacing.Twips, short.MinValue, short.MaxValue));
        if (format.Scale is { } scale)
            writer.UInt16(SprmCode.CharacterScale, (ushort)Math.Clamp(scale, 1, 600));
        if (format.Highlight is { } highlight and not HighlightColor.None)
            writer.Byte(SprmCode.Highlight, (byte)highlight);
        if (format.VerticalAlignment is { } vertical)
            writer.Byte(SprmCode.VerticalAlignment, (byte)vertical);
        if (format.Color is { Kind: ColorKind.Rgb } color)
            writer.Int32(SprmCode.ColorTrue, unchecked((int)SwapRgb(color.Rgb)));

        WriteFont(writer, SprmCode.FontAscii, format.FontAscii ?? format.FontHighAnsi, fonts);
        WriteFont(writer, SprmCode.FontEastAsia, format.FontEastAsia, fonts);
        WriteFont(writer, SprmCode.FontComplexScript, format.FontComplexScript, fonts);

        return writer.ToArray();
    }

    /// <summary>Builds the paragraph modifiers, excluding the style index that precedes them.</summary>
    /// <param name="format">The formatting to encode.</param>
    /// <param name="flags">Table structure facts that live alongside the formatting.</param>
    public static byte[] BuildParagraph(ParagraphFormat format, DocParagraphFlags flags = default)
    {
        var writer = new GrpprlWriter();

        if (format.Alignment is { } alignment)
            writer.Byte(SprmCode.Alignment, AlignmentCode(alignment));
        if (format.IndentLeft is { } left)
            writer.Int16(SprmCode.IndentLeft, Clamp(left));
        if (format.IndentRight is { } right)
            writer.Int16(SprmCode.IndentRight, Clamp(right));

        // A hanging indent is the same modifier as a first-line indent with a negative value,
        // which is how the reader tells them apart.
        if (format.IndentHanging is { } hanging)
            writer.Int16(SprmCode.IndentFirstLine, (short)-Clamp(hanging));
        else if (format.IndentFirstLine is { } firstLine)
            writer.Int16(SprmCode.IndentFirstLine, Clamp(firstLine));

        if (format.SpacingBefore is { } before)
            writer.UInt16(SprmCode.SpacingBefore, (ushort)Math.Clamp(before.Twips, 0, ushort.MaxValue));
        if (format.SpacingAfter is { } after)
            writer.UInt16(SprmCode.SpacingAfter, (ushort)Math.Clamp(after.Twips, 0, ushort.MaxValue));
        if (format.LineSpacing is { } line)
            writer.Int32(SprmCode.LineSpacing, PackLineSpacing(line, format.LineSpacingRule));

        Toggle(writer, SprmCode.KeepLinesTogether, format.KeepLinesTogether);
        Toggle(writer, SprmCode.KeepWithNext, format.KeepWithNext);
        Toggle(writer, SprmCode.PageBreakBefore, format.PageBreakBefore);
        Toggle(writer, SprmCode.WidowControl, format.WidowControl);
        Toggle(writer, SprmCode.ContextualSpacing, format.ContextualSpacing);

        if (!format.Tabs.IsEmpty && DocTabStops.Build([.. format.Tabs]) is { Length: > 0 } tabs)
            writer.Variable(SprmCode.TabStops, tabs);

        WriteBorders(writer, format.Borders);
        if (format.Shading is { IsEmpty: false } shading)
        {
            Span<byte> painted = stackalloc byte[DocShapes.ShadingBytes];
            DocShapes.WriteShading(painted, shading);
            writer.Variable(SprmCode.ParagraphShading, painted);
        }

        if (format.OutlineLevel is { } outline)
            writer.Byte(SprmCode.OutlineLevel, (byte)Math.Clamp(outline, 0, 9));
        if (format.NumberingLevel is { } level)
            writer.Byte(SprmCode.NumberingLevel, (byte)Math.Clamp(level, 0, 8));
        if (format.NumberingId is { } numbering)
            writer.UInt16(SprmCode.NumberingId, (ushort)Math.Clamp(numbering, 0, ushort.MaxValue));

        if (flags.InTable)
            writer.Toggle(SprmCode.InTable, true);
        if (flags.IsRowEnd)
            writer.Toggle(SprmCode.RowEnd, true);
        if (flags.EndsInnerCell)
            writer.Toggle(SprmCode.InnerTableCell, true);
        if (flags.EndsInnerRow)
            writer.Toggle(SprmCode.InnerRowEnd, true);
        if (flags.TableDepth > 0)
            writer.Int32(SprmCode.TableDepth, flags.TableDepth);

        return writer.ToArray();
    }

    /// <summary>Builds the section modifiers for a page setup.</summary>
    /// <param name="properties">The page setup to encode.</param>
    public static byte[] BuildSection(SectionProperties properties)
    {
        var writer = new GrpprlWriter();
        PageMargins margins = properties.Margins;

        writer.Byte(SprmCode.SectionBreak, BreakCode(properties.Start));
        writer.Byte(SprmCode.Orientation, properties.Orientation == PageOrientation.Landscape ? (byte)2 : (byte)1);
        writer.UInt16(SprmCode.PageWidth, (ushort)Math.Clamp(properties.PageWidth.Twips, 144, 31680));
        writer.UInt16(SprmCode.PageHeight, (ushort)Math.Clamp(properties.PageHeight.Twips, 144, 31680));
        writer.UInt16(SprmCode.MarginLeft, (ushort)Math.Clamp(margins.Left.Twips, 0, 31680));
        writer.UInt16(SprmCode.MarginRight, (ushort)Math.Clamp(margins.Right.Twips, 0, 31680));
        writer.Int16(SprmCode.MarginTop, Clamp(margins.Top));
        writer.Int16(SprmCode.MarginBottom, Clamp(margins.Bottom));
        writer.UInt16(SprmCode.MarginHeader, (ushort)Math.Clamp(margins.Header.Twips, 0, 31680));
        writer.UInt16(SprmCode.MarginFooter, (ushort)Math.Clamp(margins.Footer.Twips, 0, 31680));
        if (margins.Gutter != Length.Zero)
            writer.UInt16(SprmCode.Gutter, (ushort)Math.Clamp(margins.Gutter.Twips, 0, 31680));

        if (properties.DifferentFirstPage)
            writer.Toggle(SprmCode.TitlePage, true);

        if (properties.Columns.Count > 1)
        {
            writer.UInt16(SprmCode.ColumnCount, (ushort)Math.Clamp(properties.Columns.Count - 1, 0, 43));
            writer.Int16(SprmCode.ColumnSpacing, Clamp(properties.Columns.Space));
        }

        if (properties.PageNumbering.Format is { } scheme)
            writer.Byte(SprmCode.PageNumberFormat, DocNumberFormat.Code(scheme));

        if (properties.PageNumbering.Start is { } start)
        {
            writer.Toggle(SprmCode.PageNumberRestart, true);
            writer.UInt16(SprmCode.PageNumberStart, (ushort)Math.Clamp(start, 0, 32766));
        }

        return writer.ToArray();
    }

    /// <summary>
    /// Writes the edges of a paragraph's border box. Each edge is a modifier of its own with
    /// the same four-byte shape, so a paragraph with one edge costs one modifier rather than
    /// a whole box.
    /// </summary>
    private static void WriteBorders(GrpprlWriter writer, BorderSet? borders)
    {
        if (borders is null || borders.IsEmpty)
            return;

        Edge(writer, SprmCode.ParagraphBorderTop, borders.Top);
        Edge(writer, SprmCode.ParagraphBorderLeft, borders.Left);
        Edge(writer, SprmCode.ParagraphBorderBottom, borders.Bottom);
        Edge(writer, SprmCode.ParagraphBorderRight, borders.Right);
        Edge(writer, SprmCode.ParagraphBorderBetween, borders.InsideHorizontal);
    }

    private static void Edge(GrpprlWriter writer, ushort opcode, BorderLine? line)
    {
        if (line is null || line.IsEmpty)
            return;

        Span<byte> edge = stackalloc byte[DocShapes.BorderBytes];
        DocShapes.WriteBorder(edge, line);
        writer.Int32(opcode, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(edge));
    }

    private static void Toggle(GrpprlWriter writer, ushort opcode, bool? value)
    {
        if (value is { } actual)
            writer.Toggle(opcode, actual);
    }

    private static void WriteFont(GrpprlWriter writer, ushort opcode, string? name, Func<string, int> fonts)
    {
        if (string.IsNullOrEmpty(name))
            return;

        int index = fonts(name);
        if (index >= 0)
            writer.UInt16(opcode, (ushort)index);
    }

    private static short Clamp(Length value) => (short)Math.Clamp(value.Twips, short.MinValue, short.MaxValue);

    /// <summary>
    /// Line spacing packs the height and the rule into one value: the high half says whether
    /// the height is a multiple of single spacing, and a negative height means exactly.
    /// </summary>
    private static int PackLineSpacing(Length spacing, LineSpacingRule? rule)
    {
        int twips = Math.Clamp(Math.Abs(spacing.Twips), 0, short.MaxValue);
        return rule switch
        {
            LineSpacingRule.Auto or null => (1 << 16) | (ushort)twips,
            LineSpacingRule.Exact => (ushort)(short)-twips,
            _ => (ushort)twips,
        };
    }

    /// <summary>Legacy colours are stored blue-green-red rather than red-green-blue.</summary>
    private static uint SwapRgb(uint value) =>
        ((value & 0xFF) << 16) | (value & 0xFF00) | ((value >> 16) & 0xFF);

    private static byte UnderlineCode(UnderlineStyle style) => style switch
    {
        UnderlineStyle.None => 0,
        UnderlineStyle.Words => 2,
        UnderlineStyle.Double or UnderlineStyle.WavyDouble => 3,
        UnderlineStyle.Dotted or UnderlineStyle.DottedHeavy => 4,
        UnderlineStyle.Thick => 6,
        UnderlineStyle.Dash or UnderlineStyle.DashedHeavy or UnderlineStyle.DashLong or UnderlineStyle.DashLongHeavy => 7,
        UnderlineStyle.DotDash or UnderlineStyle.DashDotHeavy => 9,
        UnderlineStyle.DotDotDash or UnderlineStyle.DashDotDotHeavy => 10,
        UnderlineStyle.Wave or UnderlineStyle.WavyHeavy => 11,
        _ => 1,
    };

    private static byte AlignmentCode(ParagraphAlignment alignment) => alignment switch
    {
        ParagraphAlignment.Center => 1,
        ParagraphAlignment.Right => 2,
        ParagraphAlignment.Justify => 3,
        ParagraphAlignment.Distribute or ParagraphAlignment.ThaiDistribute => 4,
        _ => 0,
    };

    private static byte BreakCode(SectionStart start) => start switch
    {
        SectionStart.Continuous => 0,
        SectionStart.NextColumn => 1,
        SectionStart.EvenPage => 3,
        SectionStart.OddPage => 4,
        _ => 2,
    };
}
