using System.Buffers.Binary;

namespace Quillwright.Pdf.Images;

/// <summary>
/// Decodes a device-independent bitmap: the body of a <c>.bmp</c> file, and the payload a
/// metafile carries when it draws one ([MS-WMF] 2.2.2.9).
/// </summary>
/// <remarks>
/// The header comes in two shapes and five lengths, the rows are padded to four bytes and
/// written bottom up unless the height says otherwise, and the pixels are either indices into a
/// palette, channels in an order the masks give, or — for the two compressed forms — a whole
/// JPEG or PNG file that the caller can pass through untouched.
/// </remarks>
internal static class DibReader
{
    private const int CoreHeaderSize = 12;
    private const int InfoHeaderSize = 40;

    private const uint Rgb = 0;
    private const uint RunLength8 = 1;
    private const uint RunLength4 = 2;
    private const uint BitFields = 3;
    private const uint Jpeg = 4;
    private const uint Png = 5;
    private const uint AlphaBitFields = 6;

    /// <summary>Decodes a bitmap, or gives back nothing when it is one this does not read.</summary>
    /// <param name="dib">The bitmap, starting at its header.</param>
    /// <param name="pixelOffset">
    /// Where the pixels begin, relative to the header, when the container says so; negative to
    /// work it out from the header and the palette, which is what a bare DIB requires.
    /// </param>
    public static ImageSource Read(ReadOnlySpan<byte> dib, int pixelOffset = -1)
    {
        if (dib.Length < CoreHeaderSize || !Header.TryRead(dib, out Header header))
            return ImageSource.None;

        if (header.Compression is Jpeg or Png)
            return ReadEmbedded(dib, header, pixelOffset);

        int palette = header.HeaderSize + header.MaskBytes;
        int pixels = pixelOffset >= 0 ? pixelOffset : palette + (header.PaletteCount * header.PaletteEntrySize);
        if (pixels < 0 || pixels > dib.Length)
            return ImageSource.None;

        uint[] colours = ReadPalette(dib, header, palette);
        ReadOnlySpan<byte> body = dib[pixels..];

        byte[]? indices = header.Compression switch
        {
            RunLength8 => DibRunLength.Expand(body, header.Width, header.Height, bits: 8),
            RunLength4 => DibRunLength.Expand(body, header.Width, header.Height, bits: 4),
            _ => null,
        };

        return header.Compression is RunLength8 or RunLength4
            ? FromIndices(indices, header, colours)
            : FromRows(body, header, colours);
    }

    /// <summary>A bitmap whose pixels are a whole JPEG or PNG file ([MS-WMF] 2.1.1.7).</summary>
    private static ImageSource ReadEmbedded(ReadOnlySpan<byte> dib, in Header header, int pixelOffset)
    {
        int start = pixelOffset >= 0 ? pixelOffset : header.HeaderSize;
        return start >= 0 && start < dib.Length
            ? ImageSource.FromEncoded(dib[start..].ToArray())
            : ImageSource.None;
    }

    /// <summary>Reads the colour table, in whichever of the two entry sizes the header implies.</summary>
    private static uint[] ReadPalette(ReadOnlySpan<byte> dib, in Header header, int at)
    {
        uint[] colours = new uint[header.PaletteCount];
        int size = header.PaletteEntrySize;

        for (int i = 0; i < colours.Length; i++)
        {
            int entry = at + (i * size);
            colours[i] = entry + 3 <= dib.Length
                ? (uint)((dib[entry + 2] << 16) | (dib[entry + 1] << 8) | dib[entry])
                : 0;
        }

        return colours;
    }

    /// <summary>Turns the run-length expansion, which is one index a pixel, into samples.</summary>
    private static ImageSource FromIndices(byte[]? indices, in Header header, uint[] colours)
    {
        if (indices is null)
            return ImageSource.None;

        var canvas = new Canvas(header.Width, header.Height, alpha: false);
        for (int y = 0; y < header.Height; y++)
        {
            for (int x = 0; x < header.Width; x++)
                canvas.Set(x, header.TopDown ? y : header.Height - 1 - y, Colour(colours, indices[(y * header.Width) + x]), 0xFF);
        }

        return ImageSource.FromPixels(canvas.ToImage());
    }

    /// <summary>Walks the padded rows, reading each pixel in the way the bit count calls for.</summary>
    private static ImageSource FromRows(ReadOnlySpan<byte> body, in Header header, uint[] colours)
    {
        int stride = (((header.Width * header.BitCount) + 31) / 32) * 4;
        if (stride <= 0 || (long)stride * header.Height > body.Length)
            return ImageSource.None;

        var canvas = new Canvas(header.Width, header.Height, header.HasAlpha);
        for (int y = 0; y < header.Height; y++)
        {
            ReadOnlySpan<byte> row = body.Slice(y * stride, stride);
            int target = header.TopDown ? y : header.Height - 1 - y;

            for (int x = 0; x < header.Width; x++)
            {
                (uint colour, byte alpha) = Pixel(row, x, header, colours);
                canvas.Set(x, target, colour, alpha);
            }
        }

        return ImageSource.FromPixels(canvas.ToImage());
    }

    /// <summary>One pixel, as an RGB triple packed into an integer and an alpha byte.</summary>
    private static (uint Colour, byte Alpha) Pixel(ReadOnlySpan<byte> row, int x, in Header header, uint[] colours)
    {
        switch (header.BitCount)
        {
            case 1 or 2 or 4 or 8:
            {
                int perByte = 8 / header.BitCount;
                int at = x / perByte;
                if (at >= row.Length)
                    return (0, 0xFF);

                int shift = 8 - header.BitCount - (x % perByte * header.BitCount);
                int index = (row[at] >> shift) & ((1 << header.BitCount) - 1);
                return (Colour(colours, index), 0xFF);
            }

            case 16:
            {
                int at = x * 2;
                if (at + 2 > row.Length)
                    return (0, 0xFF);

                return (header.Masks.Apply(BinaryPrimitives.ReadUInt16LittleEndian(row[at..])), 0xFF);
            }

            case 24:
            {
                int at = x * 3;
                return at + 3 > row.Length
                    ? (0, 0xFF)
                    : ((uint)((row[at + 2] << 16) | (row[at + 1] << 8) | row[at]), (byte)0xFF);
            }

            case 32:
            {
                int at = x * 4;
                if (at + 4 > row.Length)
                    return (0, 0xFF);

                uint value = BinaryPrimitives.ReadUInt32LittleEndian(row[at..]);
                return (header.Masks.Apply(value), header.Masks.Alpha(value));
            }

            default:
                return (0, 0xFF);
        }
    }

    private static uint Colour(uint[] colours, int index) =>
        (uint)index < (uint)colours.Length ? colours[index] : 0;

    /// <summary>What the bitmap's header says about it, in the terms the rest of this needs.</summary>
    private readonly record struct Header(
        int HeaderSize,
        int Width,
        int Height,
        int BitCount,
        uint Compression,
        int PaletteCount,
        int PaletteEntrySize,
        int MaskBytes,
        bool TopDown,
        ChannelMasks Masks)
    {
        /// <summary>Whether any pixel can be less than opaque.</summary>
        public bool HasAlpha => Masks.AlphaMask != 0;

        /// <summary>Reads a header of either shape, rejecting sizes that cannot be drawn.</summary>
        public static bool TryRead(ReadOnlySpan<byte> dib, out Header header)
        {
            header = default;
            int size = BinaryPrimitives.ReadInt32LittleEndian(dib);
            if (size < CoreHeaderSize || size > dib.Length)
                return false;

            bool core = size < InfoHeaderSize;
            int width = core ? BinaryPrimitives.ReadInt16LittleEndian(dib[4..]) : BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
            int height = core ? BinaryPrimitives.ReadInt16LittleEndian(dib[6..]) : BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
            int bits = core ? BinaryPrimitives.ReadUInt16LittleEndian(dib[10..]) : BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
            uint compression = core ? Rgb : BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);

            // A run of 32 000 by 32 000 pixels is four gigabytes of samples; nothing a document
            // holds is that big, and refusing it is cheaper than discovering it while allocating.
            const int MaxSide = 32_000;
            if (width is <= 0 or > MaxSide || Math.Abs(height) is 0 or > MaxSide)
                return false;
            if (bits is not (1 or 2 or 4 or 8 or 16 or 24 or 32))
                return false;

            int declared = core ? 0 : (int)BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
            int palette = bits <= 8 ? (declared > 0 ? Math.Min(declared, 1 << bits) : 1 << bits) : 0;
            int masks = compression is BitFields or AlphaBitFields && size == InfoHeaderSize
                ? (compression == AlphaBitFields ? 16 : 12)
                : 0;

            header = new Header(size, width, Math.Abs(height), bits, compression, palette, core ? 3 : 4, masks,
                height < 0, ChannelMasks.For(dib, size, bits, compression));
            return true;
        }
    }
}
