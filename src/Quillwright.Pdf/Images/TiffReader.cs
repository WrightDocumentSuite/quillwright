using System.Buffers.Binary;

namespace Quillwright.Pdf.Images;

/// <summary>
/// Decodes the first image of a baseline TIFF: the uncompressed, LZW, PackBits and deflate
/// arrangements, in grey, palette or RGB.
/// </summary>
/// <remarks>
/// A TIFF is a header pointing at a directory of tagged values, some of which point at the
/// pixels. Only the first directory is read — a multi-page fax is one image on a page — and only
/// the tags a baseline reader is required to honour. Anything else, JPEG-in-TIFF and the two
/// fax encodings above all, is declined rather than guessed at.
/// </remarks>
internal static class TiffReader
{
    private const int TagWidth = 256;
    private const int TagHeight = 257;
    private const int TagBitsPerSample = 258;
    private const int TagCompression = 259;
    private const int TagPhotometric = 262;
    private const int TagStripOffsets = 273;
    private const int TagSamplesPerPixel = 277;
    private const int TagRowsPerStrip = 278;
    private const int TagStripByteCounts = 279;
    private const int TagPlanarConfiguration = 284;
    private const int TagPredictor = 317;
    private const int TagColorMap = 320;

    /// <summary>Whether the bytes open like a TIFF, in either byte order.</summary>
    /// <param name="data">The file.</param>
    public static bool Matches(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            return false;

        return (data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00)
            || (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A);
    }

    /// <summary>Decodes the first image, or gives back nothing when it is one this declines.</summary>
    /// <param name="data">The file.</param>
    public static ImageSource Read(ReadOnlySpan<byte> data)
    {
        if (!Matches(data))
            return ImageSource.None;

        bool bigEndian = data[0] == 0x4D;
        int directory = (int)Number(data, 4, bigEndian);
        Dictionary<int, TiffEntry>? tags = ReadDirectory(data, directory, bigEndian);
        if (tags is null)
            return ImageSource.None;

        var layout = TiffLayout.From(tags, data, bigEndian);
        if (!layout.IsUsable)
            return ImageSource.None;

        byte[]? rows = ReadStrips(data, tags, layout, bigEndian);
        return rows is null ? ImageSource.None : TiffPixels.Paint(rows, layout);
    }

    /// <summary>Reads the tags of one directory, refusing one that runs past the end of the file.</summary>
    private static Dictionary<int, TiffEntry>? ReadDirectory(ReadOnlySpan<byte> data, int at, bool bigEndian)
    {
        if (at < 8 || at + 2 > data.Length)
            return null;

        int count = BigOrLittle16(data, at, bigEndian);
        if (count <= 0 || at + 2 + (count * 12) > data.Length)
            return null;

        var tags = new Dictionary<int, TiffEntry>(count);
        for (int i = 0; i < count; i++)
        {
            int entry = at + 2 + (i * 12);
            tags[BigOrLittle16(data, entry, bigEndian)] = new TiffEntry(
                BigOrLittle16(data, entry + 2, bigEndian),
                Number(data, entry + 4, bigEndian),
                entry + 8);
        }

        return tags;
    }

    /// <summary>
    /// Reads every strip and joins them into one raster, decompressing each by the scheme the
    /// directory names. A strip that will not decompress ends the image where it stands rather
    /// than losing the rows before it.
    /// </summary>
    private static byte[]? ReadStrips(
        ReadOnlySpan<byte> data, Dictionary<int, TiffEntry> tags, in TiffLayout layout, bool bigEndian)
    {
        uint[] offsets = Values(data, tags, TagStripOffsets, bigEndian);
        uint[] counts = Values(data, tags, TagStripByteCounts, bigEndian);
        if (offsets.Length == 0 || counts.Length < offsets.Length)
            return null;

        byte[] rows = new byte[(long)layout.RowBytes * layout.Height <= int.MaxValue
            ? layout.RowBytes * layout.Height
            : 0];

        if (rows.Length == 0)
            return null;

        int written = 0;
        for (int i = 0; i < offsets.Length && written < rows.Length; i++)
        {
            if (offsets[i] >= (uint)data.Length || counts[i] > (uint)data.Length - offsets[i])
                break;

            ReadOnlySpan<byte> strip = data.Slice((int)offsets[i], (int)counts[i]);
            int room = Math.Min(rows.Length - written, layout.RowBytes * layout.RowsPerStrip);
            byte[]? expanded = Decompress(strip, layout.Compression, room);
            if (expanded is null)
                break;

            int take = Math.Min(expanded.Length, rows.Length - written);
            expanded.AsSpan(0, take).CopyTo(rows.AsSpan(written));
            written += take;
        }

        if (written == 0)
            return null;

        TiffPixels.Undo(rows, layout);
        return rows;
    }

    /// <summary>Expands one strip, or gives back nothing for a scheme this does not decode.</summary>
    private static byte[]? Decompress(ReadOnlySpan<byte> strip, int compression, int room) => compression switch
    {
        1 => strip[..Math.Min(strip.Length, room)].ToArray(),
        5 => LzwDecoder.Decode(strip, minCodeSize: 8, msbFirst: true, earlyChange: true, room),
        8 or 32946 => TiffPixels.Inflate(strip, room),
        32773 => TiffPixels.UnpackBits(strip, room),
        _ => null,
    };

    /// <summary>Reads a tag whose value is a count of numbers, wherever the directory keeps them.</summary>
    internal static uint[] Values(
        ReadOnlySpan<byte> data, Dictionary<int, TiffEntry> tags, int tag, bool bigEndian)
    {
        if (!tags.TryGetValue(tag, out TiffEntry entry))
            return [];

        int size = entry.Type switch { 1 => 1, 3 => 2, 4 or 13 => 4, _ => 0 };
        if (size == 0 || entry.Count == 0 || entry.Count > 1 << 20)
            return [];

        // Up to four bytes of values live in the directory entry itself; anything longer is
        // stored elsewhere and the entry holds the address instead.
        long total = (long)size * entry.Count;
        int at = total <= 4 ? entry.At : (int)Number(data, entry.At, bigEndian);
        if (at < 0 || at + total > data.Length)
            return [];

        uint[] values = new uint[entry.Count];
        for (int i = 0; i < values.Length; i++)
        {
            int offset = at + (i * size);
            values[i] = size switch
            {
                1 => data[offset],
                2 => BigOrLittle16(data, offset, bigEndian),
                _ => Number(data, offset, bigEndian),
            };
        }

        return values;
    }

    /// <summary>Reads a tag expected to hold one number.</summary>
    internal static uint Value(
        ReadOnlySpan<byte> data, Dictionary<int, TiffEntry> tags, int tag, uint fallback, bool bigEndian)
    {
        uint[] values = Values(data, tags, tag, bigEndian);
        return values.Length > 0 ? values[0] : fallback;
    }

    private static ushort BigOrLittle16(ReadOnlySpan<byte> data, int at, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data[at..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);

    private static uint Number(ReadOnlySpan<byte> data, int at, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data[at..]) : BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);

    /// <summary>One entry of a directory, before its value is read.</summary>
    /// <param name="Type">Which of the tag types the value is written as.</param>
    /// <param name="Count">How many of them there are.</param>
    /// <param name="At">Where the value, or the address of the value, sits.</param>
    internal readonly record struct TiffEntry(ushort Type, uint Count, int At);

    /// <summary>What the directory says the pixels look like.</summary>
    /// <param name="Width">Width in pixels.</param>
    /// <param name="Height">Height in pixels.</param>
    /// <param name="Bits">Bits in one sample.</param>
    /// <param name="Samples">Samples in one pixel.</param>
    /// <param name="Photometric">What a sample value means.</param>
    /// <param name="Compression">How the strips are packed.</param>
    /// <param name="Predictor">Whether each sample is stored as its difference from the last.</param>
    /// <param name="RowsPerStrip">How many rows one strip holds.</param>
    /// <param name="Palette">The colour table, for a palette image.</param>
    internal readonly record struct TiffLayout(
        int Width,
        int Height,
        int Bits,
        int Samples,
        int Photometric,
        int Compression,
        int Predictor,
        int RowsPerStrip,
        uint[] Palette)
    {
        /// <summary>How many bytes one row of samples takes.</summary>
        public int RowBytes => ((Width * Bits * Samples) + 7) / 8;

        /// <summary>Whether this is an arrangement the decoder handles.</summary>
        public bool IsUsable =>
            Width is > 0 and <= 32_000 && Height is > 0 and <= 32_000 &&
            Bits is 1 or 2 or 4 or 8 && Samples is >= 1 and <= 4 &&
            Photometric is 0 or 1 or 2 or 3 &&
            (Photometric != 2 || (Bits == 8 && Samples >= 3)) &&
            (Photometric != 3 || Palette.Length > 0);

        /// <summary>Reads the layout out of the directory, filling in the baseline defaults.</summary>
        /// <param name="tags">The directory.</param>
        /// <param name="data">The file.</param>
        /// <param name="bigEndian">Which byte order the file is written in.</param>
        public static TiffLayout From(Dictionary<int, TiffEntry> tags, ReadOnlySpan<byte> data, bool bigEndian)
        {
            uint[] bits = Values(data, tags, TagBitsPerSample, bigEndian);
            int height = (int)Value(data, tags, TagHeight, 0, bigEndian);

            // One plane at a time is a different layout altogether, not a variation on this one.
            if (Value(data, tags, TagPlanarConfiguration, 1, bigEndian) != 1)
                return default;

            return new TiffLayout(
                (int)Value(data, tags, TagWidth, 0, bigEndian),
                height,
                bits.Length > 0 ? (int)bits[0] : 1,
                (int)Value(data, tags, TagSamplesPerPixel, bits.Length > 0 ? (uint)bits.Length : 1, bigEndian),
                (int)Value(data, tags, TagPhotometric, 1, bigEndian),
                (int)Value(data, tags, TagCompression, 1, bigEndian),
                (int)Value(data, tags, TagPredictor, 1, bigEndian),
                (int)Math.Min(Value(data, tags, TagRowsPerStrip, (uint)Math.Max(height, 1), bigEndian), int.MaxValue),
                Values(data, tags, TagColorMap, bigEndian));
        }
    }
}
