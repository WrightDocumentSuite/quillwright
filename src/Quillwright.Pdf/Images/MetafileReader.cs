using System.Buffers.Binary;

namespace Quillwright.Pdf.Images;

/// <summary>
/// Finds the bitmap inside a metafile, for the very common case of one that is a wrapper around
/// a picture rather than a drawing.
/// </summary>
/// <remarks>
/// <para>
/// A metafile is a list of drawing commands, and drawing it properly would mean a graphics
/// device this library does not have. But a scanned page, a screenshot pasted into a document
/// and a picture converted out of an older format all arrive as a metafile whose only real
/// command is "draw this bitmap here" — so the bitmap is taken out and the rest, which is
/// usually nothing but a clip and a transform, is left behind.
/// </para>
/// <para>
/// When a file holds more than one, the largest is the picture and the others are decoration;
/// a metafile that draws lines and text carries no bitmap at all and is declined, which is what
/// the diagnostics then say.
/// </para>
/// </remarks>
internal static class MetafileReader
{
    /// <summary>How many records to walk before deciding the file is not what it claims.</summary>
    private const int MaxRecords = 100_000;

    /// <summary>Whether the bytes open like a Windows metafile, placeable or not.</summary>
    /// <param name="data">The file.</param>
    public static bool IsWmf(ReadOnlySpan<byte> data) =>
        (data.Length >= 22 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x9AC6CDD7)
        || (data.Length >= 18 && data[2] == 0x09 && data[3] == 0x00 && data[0] is 0x01 or 0x02 && data[1] == 0x00);

    /// <summary>Whether the bytes open like an enhanced metafile.</summary>
    /// <param name="data">The file.</param>
    public static bool IsEmf(ReadOnlySpan<byte> data) =>
        data.Length >= 44 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 1 &&
        data.Slice(40, 4).SequenceEqual(" EMF"u8);

    /// <summary>Takes the largest bitmap out of a metafile of either kind.</summary>
    /// <param name="data">The file.</param>
    public static ImageSource Read(ReadOnlySpan<byte> data)
    {
        if (IsEmf(data))
            return Largest(data, Emf(data));

        return IsWmf(data) ? Largest(data, Wmf(data)) : ImageSource.None;
    }

    /// <summary>Decodes each bitmap the walk found and keeps the one covering the most pixels.</summary>
    private static ImageSource Largest(ReadOnlySpan<byte> data, List<Located> found)
    {
        ImageSource best = ImageSource.None;
        long area = 0;

        foreach (Located bitmap in found)
        {
            if (bitmap.At < 0 || bitmap.At >= data.Length)
                continue;

            ImageSource candidate = DibReader.Read(data[bitmap.At..], bitmap.PixelOffset);
            if (candidate.IsEmpty)
                continue;

            // A file that wrapped a JPEG has nothing better to offer, so it wins outright.
            if (candidate.Pixels is not { } pixels)
                return candidate;

            long size = (long)pixels.Width * pixels.Height;
            if (size <= area)
                continue;

            area = size;
            best = candidate;
        }

        return best;
    }

    /// <summary>
    /// Walks an enhanced metafile. Each of the four records that can carry a bitmap keeps the
    /// header and the pixels at addresses of its own, both counted from the start of the record,
    /// and all four keep them in the same two places.
    /// </summary>
    private static List<Located> Emf(ReadOnlySpan<byte> data)
    {
        const int StretchDiBits = 81;
        const int SetDiBitsToDevice = 80;
        const int BitBlt = 76;
        const int StretchBlt = 77;
        const int AlphaBlend = 114;

        var found = new List<Located>();
        int at = 0;

        for (int i = 0; i < MaxRecords && at + 8 <= data.Length; i++)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
            if (size < 8 || at + size > data.Length)
                break;

            int header = type switch
            {
                StretchDiBits or SetDiBitsToDevice => 48,
                BitBlt or StretchBlt or AlphaBlend => 84,
                _ => -1,
            };

            if (header > 0 && header + 12 <= size)
                found.Add(Locate(data.Slice(at, size), at, header));

            at += size;
        }

        return found;
    }

    /// <summary>Reads the pair of addresses a record uses to point at its bitmap.</summary>
    private static Located Locate(ReadOnlySpan<byte> record, int recordAt, int header)
    {
        int info = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[header..]);
        int bits = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(header + 8)..]);
        if (info <= 0 || info >= record.Length)
            return new Located(-1, -1);

        // The pixels usually follow the header, in which case where they start can be worked
        // out from the header alone; where they do not, the record says.
        int offset = bits > info && bits < record.Length ? bits - info : -1;
        return new Located(recordAt + info, offset);
    }

    /// <summary>
    /// Walks an ordinary metafile. Its four bitmap-drawing records each put the bitmap after a
    /// different number of parameters, and it always sits whole, header and pixels together.
    /// </summary>
    private static List<Located> Wmf(ReadOnlySpan<byte> data)
    {
        const int DibBitBlt = 0x0940;
        const int DibStretchBlt = 0x0B41;
        const int SetDibToDev = 0x0D33;
        const int StretchDib = 0x0F43;

        var found = new List<Located>();
        int at = BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x9AC6CDD7 ? 22 + 18 : 18;

        for (int i = 0; i < MaxRecords && at + 6 <= data.Length; i++)
        {
            long size = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) * 2L;
            int function = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]);
            if (size < 6 || at + size > data.Length)
                break;

            int payload = function switch
            {
                DibBitBlt => 22,
                DibStretchBlt => 26,
                SetDibToDev => 24,
                StretchDib => 28,
                _ => -1,
            };

            // A blit with no source bitmap writes the same function with a shorter record.
            if (payload > 0 && size > payload + 12)
                found.Add(new Located(at + payload, -1));

            at += (int)size;
        }

        return found;
    }

    /// <summary>Where a bitmap sits inside the file.</summary>
    /// <param name="At">Where its header begins.</param>
    /// <param name="PixelOffset">Where its pixels begin relative to that, or negative to work it out.</param>
    private readonly record struct Located(int At, int PixelOffset);
}
