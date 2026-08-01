using System.Buffers.Binary;

namespace Quillwright.Pdf.Images;

/// <summary>
/// Decodes the first frame of a GIF, which is the whole of a still one and the only part of an
/// animation a page can show.
/// </summary>
/// <remarks>
/// A GIF is a screen, a palette and a list of blocks. Only two blocks matter here: the graphic
/// control that says which palette entry stands for nothing, and the image descriptor that
/// carries the pixels. Everything else — comments, plain text, application data, later frames —
/// is stepped over by the length each block declares.
/// </remarks>
internal static class GifReader
{
    private const byte ExtensionIntroducer = 0x21;
    private const byte ImageDescriptor = 0x2C;
    private const byte Trailer = 0x3B;
    private const byte GraphicControl = 0xF9;

    /// <summary>Whether the bytes open with either of the two signatures the format has.</summary>
    /// <param name="data">The file.</param>
    public static bool Matches(ReadOnlySpan<byte> data) =>
        data.Length >= 13 && (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8));

    /// <summary>Decodes the first frame, or gives back nothing when the file will not parse.</summary>
    /// <param name="data">The file.</param>
    public static ImageSource Read(ReadOnlySpan<byte> data)
    {
        if (!Matches(data))
            return ImageSource.None;

        byte packed = data[10];
        int at = 13;
        uint[] global = (packed & 0x80) != 0 ? ReadPalette(data, ref at, 2 << (packed & 7)) : [];

        int transparent = -1;
        while (at < data.Length)
        {
            switch (data[at])
            {
                case Trailer:
                    return ImageSource.None;

                case ExtensionIntroducer when at + 1 < data.Length:
                    bool control = data[at + 1] == GraphicControl;
                    at += 2;
                    transparent = ReadExtension(data, ref at, control, transparent);
                    break;

                case ImageDescriptor:
                    return ReadFrame(data, at + 1, global, transparent);

                default:
                    return ImageSource.None;
            }
        }

        return ImageSource.None;
    }

    /// <summary>
    /// Steps over an extension, noting the transparent index when it is the one that carries it.
    /// </summary>
    /// <returns>The transparent palette index, unchanged unless this block set it.</returns>
    private static int ReadExtension(ReadOnlySpan<byte> data, ref int at, bool control, int transparent)
    {
        int result = transparent;
        while (at < data.Length && data[at] != 0)
        {
            int length = data[at];
            if (control && length >= 4 && at + 1 + length <= data.Length && (data[at + 1] & 0x01) != 0)
                result = data[at + 4];

            at += 1 + length;
        }

        at++;
        return result;
    }

    /// <summary>Reads the image descriptor and the pixels behind it.</summary>
    private static ImageSource ReadFrame(ReadOnlySpan<byte> data, int at, uint[] global, int transparent)
    {
        if (at + 9 > data.Length)
            return ImageSource.None;

        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 6)..]);
        byte packed = data[at + 8];
        at += 9;

        if (width <= 0 || height <= 0)
            return ImageSource.None;

        uint[] palette = (packed & 0x80) != 0 ? ReadPalette(data, ref at, 2 << (packed & 7)) : global;
        if (palette.Length == 0 || at >= data.Length)
            return ImageSource.None;

        int minimumCodeSize = data[at++];
        byte[] codes = ReadSubBlocks(data, ref at);
        byte[]? indices = LzwDecoder.Decode(codes, minimumCodeSize, msbFirst: false, earlyChange: false, width * height);
        if (indices is null)
            return ImageSource.None;

        bool interlaced = (packed & 0x40) != 0;
        return Paint(indices, width, height, palette, transparent, interlaced);
    }

    /// <summary>Turns palette indices into samples, following the interlace when there is one.</summary>
    private static ImageSource Paint(
        byte[] indices, int width, int height, uint[] palette, int transparent, bool interlaced)
    {
        var canvas = new Canvas(width, height, transparent >= 0);
        int[] rows = interlaced ? InterlacedRows(height) : [];

        for (int row = 0; row < height; row++)
        {
            int y = interlaced ? rows[row] : row;
            for (int x = 0; x < width; x++)
            {
                int at = (row * width) + x;
                int index = at < indices.Length ? indices[at] : 0;
                uint colour = (uint)index < (uint)palette.Length ? palette[index] : 0;
                canvas.Set(x, y, colour, index == transparent ? (byte)0 : (byte)0xFF);
            }
        }

        return ImageSource.FromPixels(canvas.ToImage());
    }

    /// <summary>The order an interlaced GIF stores its rows in: four passes, coarsest first.</summary>
    private static int[] InterlacedRows(int height)
    {
        int[] rows = new int[height];
        int at = 0;

        foreach ((int start, int step) in (ReadOnlySpan<(int, int)>)[(0, 8), (4, 8), (2, 4), (1, 2)])
        {
            for (int y = start; y < height && at < height; y += step)
                rows[at++] = y;
        }

        return rows;
    }

    /// <summary>Reads a colour table of a given number of entries, three bytes each.</summary>
    private static uint[] ReadPalette(ReadOnlySpan<byte> data, ref int at, int count)
    {
        uint[] palette = new uint[count];
        for (int i = 0; i < count; i++)
        {
            int entry = at + (i * 3);
            palette[i] = entry + 3 <= data.Length
                ? (uint)((data[entry] << 16) | (data[entry + 1] << 8) | data[entry + 2])
                : 0;
        }

        at += count * 3;
        return palette;
    }

    /// <summary>Joins the chain of length-prefixed blocks the pixels are broken into.</summary>
    private static byte[] ReadSubBlocks(ReadOnlySpan<byte> data, ref int at)
    {
        var joined = new List<byte>(1024);
        while (at < data.Length && data[at] != 0)
        {
            int length = Math.Min(data[at], data.Length - at - 1);
            joined.AddRange(data.Slice(at + 1, length));
            at += 1 + length;
        }

        at++;
        return [.. joined];
    }
}
