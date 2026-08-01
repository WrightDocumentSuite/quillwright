using System.IO.Compression;
using static Quillwright.Pdf.Images.TiffReader;

namespace Quillwright.Pdf.Images;

/// <summary>
/// The part of a TIFF that is bytes rather than structure: the two simple compressions, the
/// horizontal predictor, and turning samples into the colours they stand for.
/// </summary>
internal static class TiffPixels
{
    /// <summary>Turns a raster of samples into an image, reading each sample as the tags say.</summary>
    /// <param name="rows">Every row of samples, one after another.</param>
    /// <param name="layout">What the directory said the pixels look like.</param>
    public static ImageSource Paint(byte[] rows, in TiffLayout layout)
    {
        bool alpha = layout.Samples == (layout.Photometric == 2 ? 4 : 2);
        var canvas = new Canvas(layout.Width, layout.Height, alpha);
        int maximum = (1 << layout.Bits) - 1;

        for (int y = 0; y < layout.Height; y++)
        {
            int start = y * layout.RowBytes;
            if (start + layout.RowBytes > rows.Length)
                break;

            ReadOnlySpan<byte> row = rows.AsSpan(start, layout.RowBytes);
            for (int x = 0; x < layout.Width; x++)
            {
                int at = x * layout.Samples;
                canvas.Set(x, y, Colour(row, at, layout, maximum), Opacity(row, at, layout, alpha, maximum));
            }
        }

        return ImageSource.FromPixels(canvas.ToImage());
    }

    /// <summary>What one pixel shows, as red, green and blue in the low three bytes.</summary>
    private static uint Colour(ReadOnlySpan<byte> row, int at, in TiffLayout layout, int maximum)
    {
        switch (layout.Photometric)
        {
            case 2:
            {
                uint red = (uint)Sample(row, at, layout.Bits);
                uint green = (uint)Sample(row, at + 1, layout.Bits);
                uint blue = (uint)Sample(row, at + 2, layout.Bits);
                return (red << 16) | (green << 8) | blue;
            }

            case 3:
                return FromPalette(layout.Palette, Sample(row, at, layout.Bits), maximum + 1);

            default:
            {
                int value = Sample(row, at, layout.Bits) * 255 / Math.Max(maximum, 1);

                // Photometric zero counts up from white, which is the fax convention and the
                // reason a scanned page comes out inverted when it is ignored.
                uint grey = (uint)(layout.Photometric == 0 ? 255 - value : value);
                return (grey << 16) | (grey << 8) | grey;
            }
        }
    }

    /// <summary>How opaque one pixel is, from the extra sample when the image carries one.</summary>
    private static byte Opacity(ReadOnlySpan<byte> row, int at, in TiffLayout layout, bool alpha, int maximum)
    {
        if (!alpha)
            return 0xFF;

        int value = Sample(row, at + layout.Samples - 1, layout.Bits);
        return (byte)(value * 255 / Math.Max(maximum, 1));
    }

    /// <summary>
    /// A colour table entry, which the format stores as three runs of sixteen-bit values: every
    /// red, then every green, then every blue.
    /// </summary>
    private static uint FromPalette(uint[] palette, int index, int entries)
    {
        if (palette.Length < entries * 3 || (uint)index >= (uint)entries)
            return 0;

        uint red = palette[index] >> 8;
        uint green = palette[entries + index] >> 8;
        uint blue = palette[(entries * 2) + index] >> 8;
        return (red << 16) | (green << 8) | blue;
    }

    /// <summary>Reads one sample out of a row, at whatever width the samples are packed to.</summary>
    private static int Sample(ReadOnlySpan<byte> row, int index, int bits)
    {
        if (bits == 8)
            return index < row.Length ? row[index] : 0;

        int perByte = 8 / bits;
        int at = index / perByte;
        if (at >= row.Length)
            return 0;

        int shift = 8 - bits - (index % perByte * bits);
        return (row[at] >> shift) & ((1 << bits) - 1);
    }

    /// <summary>
    /// Undoes the horizontal predictor, which stores every sample but the first of a row as its
    /// difference from the one a pixel to the left. Only the byte-wide form is defined for a
    /// baseline reader, so anything else is left as it arrived.
    /// </summary>
    /// <param name="rows">Every row of samples, one after another.</param>
    /// <param name="layout">What the directory said the pixels look like.</param>
    public static void Undo(byte[] rows, in TiffLayout layout)
    {
        if (layout.Predictor != 2 || layout.Bits != 8)
            return;

        for (int y = 0; y * layout.RowBytes < rows.Length; y++)
        {
            int start = y * layout.RowBytes;
            int end = Math.Min(start + layout.RowBytes, rows.Length);
            for (int at = start + layout.Samples; at < end; at++)
                rows[at] = (byte)(rows[at] + rows[at - layout.Samples]);
        }
    }

    /// <summary>Expands a deflated strip, taking the zlib wrapper off when there is one.</summary>
    /// <param name="strip">The compressed bytes.</param>
    /// <param name="room">How many bytes the caller has room for.</param>
    public static byte[]? Inflate(ReadOnlySpan<byte> strip, int room)
    {
        byte[] compressed = strip.ToArray();
        return Inflate<ZLibStream>(compressed, room) ?? Inflate<DeflateStream>(compressed, room);

        static byte[]? Inflate<T>(byte[] compressed, int room)
            where T : Stream
        {
            try
            {
                using var source = new MemoryStream(compressed);
                using Stream decompressor = typeof(T) == typeof(ZLibStream)
                    ? new ZLibStream(source, CompressionMode.Decompress)
                    : new DeflateStream(source, CompressionMode.Decompress);

                byte[] output = new byte[room];
                int read = decompressor.ReadAtLeast(output, room, throwOnEndOfStream: false);
                return read > 0 ? output[..read] : null;
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Expands a PackBits strip: a control byte says either how many literal bytes follow or how
    /// many times the next byte repeats.
    /// </summary>
    /// <param name="strip">The compressed bytes.</param>
    /// <param name="room">How many bytes the caller has room for.</param>
    public static byte[]? UnpackBits(ReadOnlySpan<byte> strip, int room)
    {
        byte[] output = new byte[room];
        int written = 0, at = 0;

        while (at < strip.Length && written < room)
        {
            sbyte control = (sbyte)strip[at++];
            if (control == -128)
                continue;

            if (control >= 0)
            {
                int count = Math.Min(control + 1, Math.Min(strip.Length - at, room - written));
                strip.Slice(at, count).CopyTo(output.AsSpan(written));
                written += count;
                at += control + 1;
                continue;
            }

            if (at >= strip.Length)
                break;

            int repeat = Math.Min(1 - control, room - written);
            output.AsSpan(written, repeat).Fill(strip[at++]);
            written += repeat;
        }

        return written == 0 ? null : output[..written];
    }
}
