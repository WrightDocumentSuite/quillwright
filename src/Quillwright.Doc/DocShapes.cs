using System.Buffers.Binary;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>
/// Converts the two small structures that describe how something is painted — a border edge
/// ([MS-DOC] 2.9.17, <c>Brc80</c>) and a background ([MS-DOC] 2.9.243, <c>Shd</c>).
/// </summary>
/// <remarks>
/// Both appear in several places — around a paragraph, around a table cell, inside a row
/// definition — so they are converted here rather than in each of them. Colours are stored
/// blue-green-red, and the older border form has only the sixteen-colour palette to name them
/// with, which is why an exact colour survives the newer form and not the older one.
/// </remarks>
internal static class DocShapes
{
    /// <summary>Bytes of a border edge in the older four-byte form.</summary>
    public const int BorderBytes = 4;

    /// <summary>Bytes of a background.</summary>
    public const int ShadingBytes = 10;

    private static readonly uint[] Palette =
    [
        0x000000, 0x000000, 0x0000FF, 0x00FFFF, 0x00FF00, 0xFF00FF, 0xFF0000, 0xFFFF00,
        0xFFFFFF, 0x000080, 0x008080, 0x008000, 0x800080, 0x800000, 0x808000, 0x808080, 0xC0C0C0,
    ];

    /// <summary>Writes a border edge, leaving the four bytes cleared when there is nothing to draw.</summary>
    /// <param name="destination">Where to write, at least four bytes.</param>
    /// <param name="line">The edge, or <see langword="null"/> for none.</param>
    public static void WriteBorder(Span<byte> destination, BorderLine? line)
    {
        destination[..BorderBytes].Clear();
        if (line is null || line.IsEmpty || line.Style is BorderStyle.Nil or BorderStyle.None)
            return;

        destination[0] = (byte)Math.Clamp(line.Width.EighthPoints is var width and > 0 ? width : 4, 2, 0xFF);
        destination[1] = BorderTypeCode(line.Style);
        destination[2] = PaletteIndex(line.Color);
        destination[3] = (byte)Math.Clamp(line.Space.Points, 0, 31);
    }

    /// <summary>Reads a border edge, or returns <see langword="null"/> when it draws nothing.</summary>
    /// <param name="source">The four bytes of the edge.</param>
    public static BorderLine? ReadBorder(ReadOnlySpan<byte> source)
    {
        if (source.Length < BorderBytes || source[1] == 0)
            return null;

        return new BorderLine
        {
            Style = BorderStyleOf(source[1]),
            Width = Length.FromEighthPoints(Math.Max(2, (int)source[0])),
            Color = PaletteColor(source[2]),
            Space = Length.FromPoints(source[3] & 0x1F),
        };
    }

    /// <summary>Writes a background.</summary>
    /// <param name="destination">Where to write, at least ten bytes.</param>
    /// <param name="shading">The background, or <see langword="null"/> for none.</param>
    public static void WriteShading(Span<byte> destination, Shading? shading)
    {
        // No shading is an explicit value rather than an absence: both colours automatic and
        // the nil pattern, which is what tells it apart from a background cleared on purpose.
        Shading painted = shading is null || shading.IsEmpty ? Styles.Shading.None : shading;

        BinaryPrimitives.WriteUInt32LittleEndian(destination, ColorValue(painted.Color));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], ColorValue(painted.Fill));
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], PatternCode(painted.Pattern));
    }

    /// <summary>Reads a background, or returns <see langword="null"/> when it paints nothing.</summary>
    /// <param name="source">The ten bytes of the background.</param>
    public static Shading? ReadShading(ReadOnlySpan<byte> source)
    {
        if (source.Length < ShadingBytes)
            return null;

        var shading = new Shading
        {
            Color = ColorOf(BinaryPrimitives.ReadUInt32LittleEndian(source)),
            Fill = ColorOf(BinaryPrimitives.ReadUInt32LittleEndian(source[4..])),
            Pattern = PatternOf(BinaryPrimitives.ReadUInt16LittleEndian(source[8..])),
        };

        return shading.IsEmpty ? null : shading;
    }

    /// <summary>Legacy colours are stored blue-green-red, with the top byte marking automatic.</summary>
    private static uint ColorValue(WordColor color) =>
        color.Kind == ColorKind.Rgb
            ? ((color.Rgb & 0xFF) << 16) | (color.Rgb & 0xFF00) | ((color.Rgb >> 16) & 0xFF)
            : 0xFF000000;

    private static WordColor ColorOf(uint value) =>
        (value & 0xFF000000) != 0
            ? WordColor.Auto
            : WordColor.FromRgb(((value & 0xFF) << 16) | (value & 0xFF00) | ((value >> 16) & 0xFF));

    /// <summary>The nearest of the sixteen colours the older border form can name.</summary>
    private static byte PaletteIndex(WordColor color)
    {
        if (color.Kind != ColorKind.Rgb)
            return 0;

        for (int i = 1; i < Palette.Length; i++)
        {
            if (Palette[i] == color.Rgb)
                return (byte)i;
        }

        return 1;
    }

    private static WordColor PaletteColor(byte index) =>
        index == 0 || index >= Palette.Length ? WordColor.Auto : WordColor.FromRgb(Palette[index]);

    private static byte BorderTypeCode(BorderStyle style) => style switch
    {
        BorderStyle.Single or BorderStyle.Thick => 0x01,
        BorderStyle.Double => 0x03,
        BorderStyle.Dotted => 0x06,
        BorderStyle.Dashed => 0x07,
        BorderStyle.DotDash => 0x08,
        BorderStyle.DotDotDash => 0x09,
        BorderStyle.Triple => 0x0A,
        BorderStyle.ThinThickSmallGap => 0x0B,
        BorderStyle.ThickThinSmallGap => 0x0C,
        BorderStyle.ThinThickThinSmallGap => 0x0D,
        BorderStyle.ThinThickMediumGap => 0x0E,
        BorderStyle.ThickThinMediumGap => 0x0F,
        BorderStyle.Wave => 0x14,
        BorderStyle.DoubleWave => 0x15,
        _ => 0x01,
    };

    private static BorderStyle BorderStyleOf(byte code) => code switch
    {
        0x03 => BorderStyle.Double,
        0x06 => BorderStyle.Dotted,
        0x07 => BorderStyle.Dashed,
        0x08 => BorderStyle.DotDash,
        0x09 => BorderStyle.DotDotDash,
        0x0A => BorderStyle.Triple,
        0x0B => BorderStyle.ThinThickSmallGap,
        0x0C => BorderStyle.ThickThinSmallGap,
        0x0D => BorderStyle.ThinThickThinSmallGap,
        0x0E => BorderStyle.ThinThickMediumGap,
        0x0F => BorderStyle.ThickThinMediumGap,
        0x14 => BorderStyle.Wave,
        0x15 => BorderStyle.DoubleWave,
        _ => BorderStyle.Single,
    };

    private static ushort PatternCode(ShadingPattern pattern) => pattern switch
    {
        ShadingPattern.Nil => 0xFFFF,
        ShadingPattern.Solid => 0x0001,
        ShadingPattern.HorizontalStripe => 0x0003,
        ShadingPattern.VerticalStripe => 0x0004,
        ShadingPattern.DiagonalStripe => 0x0005,
        ShadingPattern.ReverseDiagonalStripe => 0x0006,
        ShadingPattern.HorizontalCross => 0x0007,
        ShadingPattern.DiagonalCross => 0x0008,
        _ => 0x0000,
    };

    private static ShadingPattern PatternOf(ushort code) => code switch
    {
        0xFFFF => ShadingPattern.Nil,
        0x0001 => ShadingPattern.Solid,
        0x0003 => ShadingPattern.HorizontalStripe,
        0x0004 => ShadingPattern.VerticalStripe,
        0x0005 => ShadingPattern.DiagonalStripe,
        0x0006 => ShadingPattern.ReverseDiagonalStripe,
        0x0007 => ShadingPattern.HorizontalCross,
        0x0008 => ShadingPattern.DiagonalCross,
        _ => ShadingPattern.Clear,
    };
}
