using System.Buffers.Binary;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Builds the image formats a PDF cannot carry as they stand, so the decoder tests do not depend
/// on a file on disk or on a library that would encode them the same way it reads them.
/// </summary>
public static partial class Raster
{
    /// <summary>A twenty-four bit bitmap of one colour, stored the usual way up: bottom row first.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="red">The red channel of every pixel.</param>
    /// <param name="green">The green channel.</param>
    /// <param name="blue">The blue channel.</param>
    /// <param name="topDown">Whether to declare the rows the other way up.</param>
    public static byte[] Bmp(int width, int height, byte red, byte green, byte blue, bool topDown = false)
    {
        byte[] dib = Dib24(width, height, red, green, blue, topDown);
        byte[] file = new byte[14 + dib.Length];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), 14 + 40);
        dib.CopyTo(file.AsSpan(14));
        return file;
    }

    /// <summary>A bitmap with no file header, which is how a metafile carries one.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="red">The red channel of every pixel.</param>
    /// <param name="green">The green channel.</param>
    /// <param name="blue">The blue channel.</param>
    /// <param name="topDown">Whether to declare the rows the other way up.</param>
    public static byte[] Dib24(int width, int height, byte red, byte green, byte blue, bool topDown = false)
    {
        int stride = ((width * 3) + 3) / 4 * 4;
        byte[] dib = new byte[40 + (stride * height)];

        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), topDown ? -height : height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int at = 40 + (y * stride) + (x * 3);
                dib[at] = blue;
                dib[at + 1] = green;
                dib[at + 2] = red;
            }
        }

        return dib;
    }

    /// <summary>An eight-bit bitmap whose left half is one palette entry and right half another.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public static byte[] BmpPalette(int width, int height)
    {
        int stride = (width + 3) / 4 * 4;
        byte[] dib = new byte[40 + (256 * 4) + (stride * height)];

        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 8);

        // Entry one is red, entry two is green; the table is written blue first.
        dib[40 + 4 + 2] = 0xFF;
        dib[40 + 8 + 1] = 0xFF;

        int pixels = 40 + (256 * 4);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                dib[pixels + (y * stride) + x] = (byte)(x < width / 2 ? 1 : 2);
        }

        return WithFileHeader(dib, pixels);
    }

    /// <summary>A run-length encoded bitmap: every row one run of the first palette entry.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public static byte[] BmpRunLength(int width, int height)
    {
        var body = new List<byte>();
        for (int y = 0; y < height; y++)
        {
            body.AddRange([(byte)width, 1]);
            body.AddRange([0, (byte)(y == height - 1 ? 1 : 0)]);
        }

        byte[] dib = new byte[40 + (256 * 4) + body.Count];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 1);

        dib[40 + 4 + 2] = 0xFF;
        body.CopyTo(dib, 40 + (256 * 4));
        return WithFileHeader(dib, 40 + (256 * 4));
    }

    private static byte[] WithFileHeader(byte[] dib, int pixelOffset)
    {
        byte[] file = new byte[14 + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), 14 + pixelOffset);
        dib.CopyTo(file.AsSpan(14));
        return file;
    }

    /// <summary>
    /// A GIF whose left half is the first palette entry and right half the second, packed the
    /// way an encoder that never builds a dictionary would: literals only, with the code reset
    /// sent before the width would have to grow.
    /// </summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="transparent">Which palette entry stands for nothing, or negative for none.</param>
    public static byte[] Gif(int width, int height, int transparent = -1)
    {
        var file = new List<byte>();
        file.AddRange("GIF89a"u8);
        file.AddRange([(byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0xF7, 0, 0]);

        for (int i = 0; i < 256; i++)
        {
            file.Add(i == 1 ? (byte)0xFF : (byte)0);
            file.Add(i == 2 ? (byte)0xFF : (byte)0);
            file.Add(0);
        }

        if (transparent >= 0)
            file.AddRange([0x21, 0xF9, 4, 0x01, 0, 0, (byte)transparent, 0]);

        file.AddRange([0x2C, 0, 0, 0, 0, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0]);
        file.Add(8);

        byte[] indices = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                indices[(y * width) + x] = (byte)(x < width / 2 ? 1 : 2);
        }

        foreach (byte[] block in SubBlocks(LiteralCodes(indices)))
        {
            file.Add((byte)block.Length);
            file.AddRange(block);
        }

        file.AddRange([0, 0x3B]);
        return [.. file];
    }

    /// <summary>Packs indices as nine-bit literal codes, resetting before the width would grow.</summary>
    private static byte[] LiteralCodes(byte[] indices)
    {
        var bits = new BitWriter();
        bits.Write(256, 9);

        int since = 0;
        foreach (byte index in indices)
        {
            if (since == 254)
            {
                bits.Write(256, 9);
                since = 0;
            }

            bits.Write(index, 9);
            since++;
        }

        bits.Write(257, 9);
        return bits.ToArray();
    }

    private static IEnumerable<byte[]> SubBlocks(byte[] data)
    {
        for (int at = 0; at < data.Length; at += 255)
            yield return data[at..Math.Min(at + 255, data.Length)];
    }

    /// <summary>Writes codes into a byte stream from the bottom of each byte up, as GIF does.</summary>
    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _accumulator;
        private int _bits;

        public void Write(int code, int width)
        {
            _accumulator |= code << _bits;
            _bits += width;

            while (_bits >= 8)
            {
                _bytes.Add((byte)_accumulator);
                _accumulator >>= 8;
                _bits -= 8;
            }
        }

        public byte[] ToArray()
        {
            if (_bits > 0)
                _bytes.Add((byte)_accumulator);

            return [.. _bytes];
        }
    }
}
