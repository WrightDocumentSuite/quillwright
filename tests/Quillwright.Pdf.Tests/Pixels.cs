using System.Buffers.Binary;
using System.IO.Compression;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Builds a tiny PNG in memory, so the image tests do not depend on a file on disk.
/// </summary>
internal static class Pixels
{
    /// <summary>A solid opaque image of the given size.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="red">The red channel of every pixel.</param>
    /// <param name="green">The green channel.</param>
    /// <param name="blue">The blue channel.</param>
    public static byte[] Png(int width, int height, byte red = 0x30, byte green = 0x70, byte blue = 0xC0)
    {
        byte[] raw = new byte[height * ((width * 3) + 1)];
        int at = 0;

        for (int y = 0; y < height; y++)
        {
            raw[at++] = 0; // No filter on this row.
            for (int x = 0; x < width; x++)
            {
                raw[at++] = red;
                raw[at++] = green;
                raw[at++] = blue;
            }
        }

        var file = new MemoryStream();
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // Eight bits per channel.
        header[9] = 2;  // Truecolour.
        Chunk(file, "IHDR", header);

        Chunk(file, "IDAT", Deflate(raw));
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return compressed.ToArray();
    }

    private static void Chunk(Stream file, string tag, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        file.Write(length);

        byte[] body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++)
            body[i] = (byte)tag[i];

        data.CopyTo(body.AsSpan(4));
        file.Write(body);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        file.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(int)(crc & 1));
        }

        return crc ^ 0xFFFFFFFF;
    }
}
