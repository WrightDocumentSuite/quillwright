using System.Buffers.Binary;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>
/// Turns the property modifiers of a legacy document into the model's format records.
/// </summary>
/// <remarks>
/// Only the properties that survive the trip to <c>.docx</c> as something a reader would
/// notice are translated: the typography, the paragraph geometry, the style reference and
/// the table structure. Everything else is skipped, which the packed encoding makes safe.
/// </remarks>
internal static partial class SprmTranslator
{
    private static readonly uint[] ColorPalette =
    [
        0x000000, 0x000000, 0x0000FF, 0x00FFFF, 0x00FF00, 0xFF00FF, 0xFF0000, 0xFFFF00,
        0xFFFFFF, 0x000080, 0x008080, 0x008000, 0x800080, 0x800000, 0x808000, 0x808080, 0xC0C0C0,
    ];

    /// <summary>Applies the character modifiers of a property list.</summary>
    /// <param name="format">The formatting in force before this list.</param>
    /// <param name="properties">The packed modifiers.</param>
    /// <param name="fonts">Resolves a font index to its name.</param>
    /// <param name="styles">Resolves a style index to its identifier.</param>
    public static RunFormat ApplyRun(
        RunFormat format,
        ReadOnlySpan<byte> properties,
        DocFontTable fonts,
        DocStyleSheet? styles = null)
    {
        var reader = new SprmReader(properties);
        while (reader.TryRead(out Sprm sprm))
        {
            format = sprm.Opcode switch
            {
                SprmCode.Bold => format with { Bold = sprm.Toggle(format.Bold) },
                SprmCode.Italic => format with { Italic = sprm.Toggle(format.Italic) },
                SprmCode.Strike => format with { Strike = sprm.Toggle(format.Strike) },
                SprmCode.Outline => format with { Outline = sprm.Toggle(format.Outline) },
                SprmCode.Shadow => format with { Shadow = sprm.Toggle(format.Shadow) },
                SprmCode.SmallCaps => format with { SmallCaps = sprm.Toggle(format.SmallCaps) },
                SprmCode.Caps => format with { Caps = sprm.Toggle(format.Caps) },
                SprmCode.Hidden => format with { Hidden = sprm.Toggle(format.Hidden) },
                SprmCode.DoubleStrike => format with { DoubleStrike = sprm.Toggle(format.DoubleStrike) },
                SprmCode.Imprint => format with { Imprint = sprm.Toggle(format.Imprint) },
                SprmCode.Emboss => format with { Emboss = sprm.Toggle(format.Emboss) },
                SprmCode.NoProof => format with { NoProof = sprm.Toggle(format.NoProof) },
                SprmCode.WebHidden => format with { WebHidden = sprm.Toggle(format.WebHidden) },
                SprmCode.BoldComplexScript => format with { BoldComplexScript = sprm.Toggle(format.BoldComplexScript) },
                SprmCode.ItalicComplexScript => format with { ItalicComplexScript = sprm.Toggle(format.ItalicComplexScript) },
                SprmCode.Underline => format with { Underline = TranslateUnderline(sprm.Byte) },
                SprmCode.FontSize => format with { Size = Length.FromHalfPoints(sprm.UInt16) },
                SprmCode.FontSizeComplexScript => format with { SizeComplexScript = Length.FromHalfPoints(sprm.UInt16) },
                SprmCode.Position => format with { Position = Length.FromHalfPoints(sprm.Int16) },
                SprmCode.Kerning => format with { Kerning = Length.FromHalfPoints(sprm.UInt16) },
                SprmCode.CharacterSpacing => format with { CharacterSpacing = Length.FromTwips(sprm.Int16) },
                SprmCode.CharacterScale => format with { Scale = sprm.UInt16 },
                SprmCode.ColorIndexed => format with { Color = PaletteColor(sprm.Byte) },
                SprmCode.ColorTrue => format with { Color = WordColor.FromRgb(SwapRgb((uint)sprm.Int32)) },
                SprmCode.Highlight => format with { Highlight = TranslateHighlight(sprm.Byte) },
                SprmCode.CharacterStyle => format with { StyleId = styles?.Identifier(sprm.UInt16) },
                SprmCode.FontAscii => format with { FontAscii = fonts.Name(sprm.UInt16), FontHighAnsi = fonts.Name(sprm.UInt16) },
                SprmCode.FontEastAsia => format with { FontEastAsia = fonts.Name(sprm.UInt16) },
                SprmCode.FontComplexScript => format with { FontComplexScript = fonts.Name(sprm.UInt16) },
                SprmCode.VerticalAlignment => format with { VerticalAlignment = TranslateVerticalAlign(sprm.Byte) },
                _ => format,
            };
        }

        return format;
    }

    /// <summary>Applies the paragraph modifiers of a property list.</summary>
    public static ParagraphFormat ApplyParagraph(ParagraphFormat format, ReadOnlySpan<byte> properties, out DocParagraphFlags flags)
    {
        flags = default;
        var reader = new SprmReader(properties);
        while (reader.TryRead(out Sprm sprm))
        {
            switch (sprm.Opcode)
            {
                case SprmCode.Alignment:
                case SprmCode.AlignmentNew:
                    format = format with { Alignment = TranslateAlignment(sprm.Byte) };
                    break;
                case SprmCode.IndentLeft:
                case SprmCode.IndentLeftNew:
                    format = format with { IndentLeft = Length.FromTwips(sprm.Int16) };
                    break;
                case SprmCode.IndentRight:
                case SprmCode.IndentRightNew:
                    format = format with { IndentRight = Length.FromTwips(sprm.Int16) };
                    break;
                case SprmCode.IndentFirstLine:
                    format = sprm.Int16 < 0
                        ? format with { IndentHanging = Length.FromTwips(-sprm.Int16) }
                        : format with { IndentFirstLine = Length.FromTwips(sprm.Int16) };
                    break;
                case SprmCode.SpacingBefore:
                    format = format with { SpacingBefore = Length.FromTwips(sprm.UInt16) };
                    break;
                case SprmCode.SpacingAfter:
                    format = format with { SpacingAfter = Length.FromTwips(sprm.UInt16) };
                    break;
                case SprmCode.LineSpacing:
                    format = ApplyLineSpacing(format, sprm.Int32);
                    break;
                case SprmCode.KeepLinesTogether:
                    format = format with { KeepLinesTogether = sprm.Byte != 0 };
                    break;
                case SprmCode.KeepWithNext:
                    format = format with { KeepWithNext = sprm.Byte != 0 };
                    break;
                case SprmCode.PageBreakBefore:
                    format = format with { PageBreakBefore = sprm.Byte != 0 };
                    break;
                case SprmCode.WidowControl:
                    format = format with { WidowControl = sprm.Byte != 0 };
                    break;
                case SprmCode.ContextualSpacing:
                    format = format with { ContextualSpacing = sprm.Byte != 0 };
                    break;
                case SprmCode.OutlineLevel:
                    format = format with { OutlineLevel = sprm.Byte >= 9 ? null : sprm.Byte };
                    break;
                case SprmCode.TabStops:
                    format = format with { Tabs = DocTabStops.Read(sprm.Operand) };
                    break;
                case SprmCode.ParagraphBorderTop:
                case SprmCode.ParagraphBorderLeft:
                case SprmCode.ParagraphBorderBottom:
                case SprmCode.ParagraphBorderRight:
                case SprmCode.ParagraphBorderBetween:
                    format = format with { Borders = ApplyBorder(format.Borders, sprm) };
                    break;
                case SprmCode.ParagraphShading when sprm.Operand.Length >= 1 + DocShapes.ShadingBytes:
                    format = format with { Shading = DocShapes.ReadShading(sprm.Operand[1..]) };
                    break;
                case SprmCode.NumberingLevel:
                    format = format with { NumberingLevel = sprm.Byte };
                    break;
                case SprmCode.NumberingId:
                    format = format with { NumberingId = sprm.UInt16 };
                    break;
                case SprmCode.InTable:
                    flags = flags with { InTable = sprm.Byte != 0 };
                    break;
                case SprmCode.RowEnd:
                    flags = flags with { IsRowEnd = sprm.Byte != 0 };
                    break;
                case SprmCode.TableDepth:
                    flags = flags with { TableDepth = sprm.Int32 };
                    break;
                case SprmCode.InnerTableCell:
                    flags = flags with { EndsInnerCell = sprm.Byte != 0 };
                    break;
                case SprmCode.InnerRowEnd:
                    flags = flags with { EndsInnerRow = sprm.Byte != 0 };
                    break;
            }
        }

        return format;
    }


    /// <summary>Sets one edge of a paragraph's border box, leaving the others as they were.</summary>
    private static BorderSet ApplyBorder(BorderSet? borders, Sprm sprm)
    {
        Span<byte> edge = stackalloc byte[DocShapes.BorderBytes];
        BinaryPrimitives.WriteInt32LittleEndian(edge, sprm.Int32);
        BorderLine? line = DocShapes.ReadBorder(edge);
        BorderSet set = borders ?? BorderSet.Empty;

        return sprm.Opcode switch
        {
            SprmCode.ParagraphBorderTop => set with { Top = line },
            SprmCode.ParagraphBorderLeft => set with { Left = line },
            SprmCode.ParagraphBorderBottom => set with { Bottom = line },
            SprmCode.ParagraphBorderRight => set with { Right = line },
            _ => set with { InsideHorizontal = line },
        };
    }

    private static ParagraphFormat ApplyLineSpacing(ParagraphFormat format, int packed)
    {
        short value = (short)(packed & 0xFFFF);
        bool multiple = (packed >> 16) != 0;
        return format with
        {
            LineSpacing = Length.FromTwips(Math.Abs(value)),
            LineSpacingRule = multiple ? LineSpacingRule.Auto : value < 0 ? LineSpacingRule.Exact : LineSpacingRule.AtLeast,
        };
    }

    private static WordColor PaletteColor(byte index) =>
        index == 0 || index >= ColorPalette.Length ? WordColor.Auto : WordColor.FromRgb(ColorPalette[index]);

    /// <summary>Legacy colours are stored blue-green-red rather than red-green-blue.</summary>
    private static uint SwapRgb(uint value) =>
        ((value & 0xFF) << 16) | (value & 0xFF00) | ((value >> 16) & 0xFF);

    private static UnderlineStyle TranslateUnderline(byte value) => value switch
    {
        0 => UnderlineStyle.None,
        1 => UnderlineStyle.Single,
        2 => UnderlineStyle.Words,
        3 => UnderlineStyle.Double,
        4 => UnderlineStyle.Dotted,
        6 => UnderlineStyle.Thick,
        7 => UnderlineStyle.Dash,
        9 => UnderlineStyle.DotDash,
        10 => UnderlineStyle.DotDotDash,
        11 => UnderlineStyle.Wave,
        _ => UnderlineStyle.Single,
    };

    private static HighlightColor TranslateHighlight(byte value) =>
        value < 17 ? (HighlightColor)value : HighlightColor.None;

    private static ParagraphAlignment TranslateAlignment(byte value) => value switch
    {
        1 => ParagraphAlignment.Center,
        2 => ParagraphAlignment.Right,
        3 => ParagraphAlignment.Justify,
        4 => ParagraphAlignment.Distribute,
        _ => ParagraphAlignment.Left,
    };

    private static VerticalTextAlignment TranslateVerticalAlign(byte value) => value switch
    {
        1 => VerticalTextAlignment.Superscript,
        2 => VerticalTextAlignment.Subscript,
        _ => VerticalTextAlignment.Baseline,
    };
}

/// <summary>Structural facts about a paragraph that do not belong in its formatting.</summary>
/// <param name="InTable">Whether the paragraph is a table cell's content.</param>
/// <param name="IsRowEnd">Whether the paragraph mark ends a table row at the outermost depth.</param>
/// <param name="TableDepth">Nesting depth for tables inside tables; one for an unnested table.</param>
/// <param name="EndsInnerCell">Whether the mark ends a cell of a nested table.</param>
/// <param name="EndsInnerRow">Whether the mark ends a row of a nested table.</param>
/// <remarks>
/// A nested table cannot use the cell character, because the cell it sits in would read that
/// as its own boundary. Nested marks are ordinary paragraph marks distinguished by the last
/// two flags, which is why depth changes not just the numbering but the encoding.
/// </remarks>
internal readonly record struct DocParagraphFlags(
    bool InTable,
    bool IsRowEnd,
    int TableDepth,
    bool EndsInnerCell = false,
    bool EndsInnerRow = false);
