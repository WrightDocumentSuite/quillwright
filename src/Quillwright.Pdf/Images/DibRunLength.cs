namespace Quillwright.Pdf.Images;

/// <summary>
/// Expands the two run-length encodings a bitmap can use ([MS-WMF] 2.1.1.5) into one palette
/// index a pixel.
/// </summary>
/// <remarks>
/// Both encodings are a stream of pairs. A pair whose first byte is not zero repeats a colour;
/// one whose first byte is zero is an instruction — end the row, end the image, jump, or copy a
/// run of literal pixels that follows. Anything the stream does not reach keeps the index it
/// started with, which is the first entry of the palette, as the format intends.
/// </remarks>
internal static class DibRunLength
{
    private const byte EndOfLine = 0;
    private const byte EndOfBitmap = 1;
    private const byte Delta = 2;

    /// <summary>Expands a run-length bitmap, or gives back nothing when the stream is malformed.</summary>
    /// <param name="body">The encoded pixels.</param>
    /// <param name="width">Width of the image in pixels.</param>
    /// <param name="height">Height of the image in pixels.</param>
    /// <param name="bits">Four or eight, the width of one index.</param>
    public static byte[]? Expand(ReadOnlySpan<byte> body, int width, int height, int bits)
    {
        byte[] pixels = new byte[width * height];
        int x = 0, y = 0, at = 0;

        while (at + 1 < body.Length)
        {
            byte count = body[at];
            byte value = body[at + 1];
            at += 2;

            if (count > 0)
            {
                Repeat(pixels, width, height, ref x, y, count, value, bits);
                continue;
            }

            switch (value)
            {
                case EndOfLine:
                    x = 0;
                    y++;
                    break;

                case EndOfBitmap:
                    return pixels;

                case Delta when at + 1 < body.Length:
                    x += body[at];
                    y += body[at + 1];
                    at += 2;
                    break;

                case Delta:
                    return pixels;

                default:
                    at = Literal(body, at, pixels, width, height, ref x, y, value, bits);
                    break;
            }

            if (y >= height)
                return pixels;
        }

        return pixels;
    }

    /// <summary>Writes one colour across a run of pixels.</summary>
    private static void Repeat(byte[] pixels, int width, int height, ref int x, int y, int count, byte value, int bits)
    {
        for (int i = 0; i < count; i++, x++)
        {
            byte index = bits == 8 ? value : (byte)((i % 2 == 0 ? value >> 4 : value) & 0x0F);
            Put(pixels, width, height, x, y, index);
        }
    }

    /// <summary>
    /// Copies a run of literal pixels, which is padded out to an even number of bytes before the
    /// next pair begins.
    /// </summary>
    /// <returns>Where the next pair starts.</returns>
    private static int Literal(
        ReadOnlySpan<byte> body, int at, byte[] pixels, int width, int height, ref int x, int y, int count, int bits)
    {
        int bytes = bits == 8 ? count : (count + 1) / 2;
        if (at + bytes > body.Length)
            return body.Length;

        for (int i = 0; i < count; i++, x++)
        {
            byte packed = body[at + (bits == 8 ? i : i / 2)];
            byte index = bits == 8 ? packed : (byte)((i % 2 == 0 ? packed >> 4 : packed) & 0x0F);
            Put(pixels, width, height, x, y, index);
        }

        return at + bytes + (bytes % 2);
    }

    private static void Put(byte[] pixels, int width, int height, int x, int y, byte index)
    {
        if ((uint)x < (uint)width && (uint)y < (uint)height)
            pixels[(y * width) + x] = index;
    }
}
