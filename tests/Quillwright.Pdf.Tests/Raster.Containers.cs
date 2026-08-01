using System.Buffers.Binary;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// The formats that hold an image rather than being one: a TIFF directory, and the two metafiles
/// that wrap a bitmap in a drawing command.
/// </summary>
public static partial class Raster
{
    /// <summary>How a TIFF strip is packed.</summary>
    public enum TiffPacking
    {
        /// <summary>Stored as it is.</summary>
        None,

        /// <summary>The run-length scheme baseline TIFF calls PackBits.</summary>
        PackBits,

        /// <summary>Deflated, with the wrapper a zlib stream carries.</summary>
        Deflate,
    }

    /// <summary>An RGB TIFF of one colour, in whichever packing is asked for.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="red">The red channel of every pixel.</param>
    /// <param name="green">The green channel.</param>
    /// <param name="blue">The blue channel.</param>
    /// <param name="packing">How to pack the strip.</param>
    public static byte[] Tiff(int width, int height, byte red, byte green, byte blue, TiffPacking packing = TiffPacking.None)
    {
        byte[] rows = new byte[width * height * 3];
        for (int i = 0; i < width * height; i++)
        {
            rows[i * 3] = red;
            rows[(i * 3) + 1] = green;
            rows[(i * 3) + 2] = blue;
        }

        byte[] strip = packing switch
        {
            TiffPacking.PackBits => PackBits(rows),
            TiffPacking.Deflate => Deflate(rows),
            _ => rows,
        };

        ushort compression = packing switch
        {
            TiffPacking.PackBits => 32773,
            TiffPacking.Deflate => 8,
            _ => 1,
        };

        return Directory(width, height, strip, compression);
    }

    /// <summary>Assembles a header, a strip and the directory that describes it.</summary>
    private static byte[] Directory(int width, int height, byte[] strip, ushort compression)
    {
        // Three shorts of bits-per-sample do not fit in a directory entry, so they sit between
        // the header and the directory, where the entry can point at them.
        const int BitsAt = 8;
        int stripAt = BitsAt + 6;
        int directoryAt = stripAt + strip.Length;

        (int Tag, int Type, int Value)[] tags =
        [
            (256, 3, width),
            (257, 3, height),
            (258, 3, BitsAt),
            (259, 3, compression),
            (262, 3, 2),
            (273, 4, stripAt),
            (277, 3, 3),
            (278, 4, height),
            (279, 4, strip.Length),
            (284, 3, 1),
        ];

        byte[] file = new byte[directoryAt + 2 + (tags.Length * 12) + 4];
        file[0] = (byte)'I';
        file[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)directoryAt);

        for (int i = 0; i < 3; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(BitsAt + (i * 2)), 8);

        strip.CopyTo(file.AsSpan(stripAt));
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(directoryAt), (ushort)tags.Length);

        for (int i = 0; i < tags.Length; i++)
        {
            Span<byte> entry = file.AsSpan(directoryAt + 2 + (i * 12));
            BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)tags[i].Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], (ushort)tags[i].Type);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], tags[i].Tag == 258 ? 3u : 1u);

            if (tags[i].Type == 3 && tags[i].Tag != 258)
                BinaryPrimitives.WriteUInt16LittleEndian(entry[8..], (ushort)tags[i].Value);
            else
                BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)tags[i].Value);
        }

        return file;
    }

    /// <summary>Packs bytes the way baseline TIFF's run-length scheme does.</summary>
    private static byte[] PackBits(byte[] data)
    {
        var packed = new List<byte>();
        for (int at = 0; at < data.Length;)
        {
            int run = 1;
            while (run < 127 && at + run < data.Length && data[at + run] == data[at])
                run++;

            if (run > 1)
            {
                packed.Add((byte)(sbyte)(1 - run));
                packed.Add(data[at]);
                at += run;
                continue;
            }

            int literal = 1;
            while (literal < 127 && at + literal < data.Length && data[at + literal] != data[at + literal - 1])
                literal++;

            packed.Add((byte)(literal - 1));
            packed.AddRange(data.AsSpan(at, literal));
            at += literal;
        }

        return [.. packed];
    }

    private static byte[] Deflate(byte[] data)
    {
        var compressed = new MemoryStream();
        using (var stream = new System.IO.Compression.ZLibStream(
                   compressed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            stream.Write(data);
        }

        return compressed.ToArray();
    }

    /// <summary>A placeable metafile whose one drawing command stretches a bitmap onto the page.</summary>
    /// <param name="dib">The bitmap to wrap, header and pixels together.</param>
    public static byte[] Wmf(byte[] dib)
    {
        const int StretchDib = 0x0F43;
        int recordWords = (28 + dib.Length + 1) / 2;
        byte[] file = new byte[22 + 18 + (recordWords * 2) + 6];

        BinaryPrimitives.WriteUInt32LittleEndian(file, 0x9AC6CDD7);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(24), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26), 0x0300);

        Span<byte> record = file.AsSpan(40);
        BinaryPrimitives.WriteUInt32LittleEndian(record, (uint)recordWords);
        BinaryPrimitives.WriteUInt16LittleEndian(record[4..], StretchDib);
        dib.CopyTo(record[28..]);

        // The trailing record is the one that says the file is over.
        Span<byte> end = file.AsSpan(40 + (recordWords * 2));
        BinaryPrimitives.WriteUInt32LittleEndian(end, 3);
        return file;
    }

    /// <summary>An enhanced metafile whose one drawing command stretches a bitmap onto the page.</summary>
    /// <param name="dib">The bitmap to wrap, header and pixels together.</param>
    /// <param name="headerBytes">How many bytes of the bitmap are its header and palette.</param>
    public static byte[] Emf(byte[] dib, int headerBytes = 40)
    {
        const int Header = 1;
        const int StretchDiBits = 81;
        const int Eof = 14;

        int record = 80 + dib.Length;
        byte[] file = new byte[88 + record + 20];

        BinaryPrimitives.WriteUInt32LittleEndian(file, Header);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 88);
        " EMF"u8.CopyTo(file.AsSpan(40));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(48), (uint)file.Length);

        Span<byte> bits = file.AsSpan(88);
        BinaryPrimitives.WriteUInt32LittleEndian(bits, StretchDiBits);
        BinaryPrimitives.WriteUInt32LittleEndian(bits[4..], (uint)record);
        BinaryPrimitives.WriteUInt32LittleEndian(bits[48..], 80);
        BinaryPrimitives.WriteUInt32LittleEndian(bits[52..], (uint)headerBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bits[56..], (uint)(80 + headerBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(bits[60..], (uint)(dib.Length - headerBytes));
        dib.CopyTo(bits[80..]);

        Span<byte> end = file.AsSpan(88 + record);
        BinaryPrimitives.WriteUInt32LittleEndian(end, Eof);
        BinaryPrimitives.WriteUInt32LittleEndian(end[4..], 20);
        return file;
    }
}
