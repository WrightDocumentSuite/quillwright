using System.Buffers.Binary;
using System.IO.Compression;
using Quillwright.Model;

namespace Quillwright.Doc;

/// <summary>
/// Turns one drawing-layer image record into bytes a package can hold ([MS-ODRAW] 2.2.23,
/// <c>OfficeArtBlip</c>, and 2.2.32, <c>OfficeArtFBSE</c>).
/// </summary>
/// <remarks>
/// Every record begins with one or two digests of the pixels, which are what the drawing
/// layer recognises an image by and are of no use once the bytes are out. A bitmap follows
/// them directly; a metafile follows a header that says how large it was before it was
/// deflated, because metafiles are the one kind stored compressed.
/// </remarks>
internal static class OfficeArtBlip
{
    /// <summary>Bytes of the header that says how to process a metafile ([MS-ODRAW] 2.2.31).</summary>
    private const int MetafileHeaderBytes = 34;

    /// <summary>Bytes of a store entry before its name and the image it may carry.</summary>
    private const int EntryHeaderBytes = 36;

    private const ushort EntryType = 0xF007;
    private const int IdentityBytes = 16;

    /// <summary>Whether a record type is one the store's list is made of.</summary>
    /// <param name="type">Type of the record.</param>
    public static bool IsEntry(ushort type) => type == EntryType || IsImage(type);

    /// <summary>Whether a record type is an image rather than a reference to one.</summary>
    /// <param name="type">Type of the record.</param>
    public static bool IsImage(ushort type) => type is >= 0xF018 and <= 0xF117;

    /// <summary>
    /// The image a store entry stands for, which is either carried inside the entry or left
    /// in the delay stream — for a Word document, the stream the text is in ([MS-DOC] 2.9.171).
    /// </summary>
    /// <param name="data">The stream the entry was read from.</param>
    /// <param name="entry">The entry, either a store record or an image record.</param>
    /// <param name="delayed">The delay stream, when the file has one.</param>
    public static ImageData? Resolve(byte[] data, OfficeArtRecord entry, byte[]? delayed)
    {
        if (entry.Type != EntryType)
            return IsImage(entry.Type) ? Read(data, entry) : null;

        if (entry.Body + EntryHeaderBytes > entry.End)
            return null;

        int embedded = entry.Body + EntryHeaderBytes + data[entry.Body + 33];
        if (embedded < entry.End && OfficeArtRecord.TryRead(data, embedded, entry.End, out OfficeArtRecord carried))
            return Read(data, carried);

        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry.Body + 28));
        return delayed is not null && offset != 0xFFFFFFFF &&
               OfficeArtRecord.TryRead(delayed, (int)offset, delayed.Length, out OfficeArtRecord late)
            ? Read(delayed, late)
            : null;
    }

    /// <summary>The first image among the records between two offsets, containers included.</summary>
    /// <param name="data">The stream the records live in.</param>
    /// <param name="start">Offset of the first header.</param>
    /// <param name="end">Offset to stop at.</param>
    /// <param name="delayed">The delay stream, when the file has one.</param>
    public static ImageData? FindFirst(byte[] data, int start, int end, byte[]? delayed)
    {
        foreach (OfficeArtRecord record in OfficeArtRecord.Walk(data, start, end))
        {
            if (record.IsContainer)
            {
                if (FindFirst(data, record.Body, record.End, delayed) is { } nested)
                    return nested;
                continue;
            }

            if (IsEntry(record.Type) && Resolve(data, record, delayed) is { } image)
                return image;
        }

        return null;
    }

    /// <summary>Reads one image record, or returns <see langword="null"/> for a format not carried across.</summary>
    /// <param name="data">The stream the record was read from.</param>
    /// <param name="image">The record.</param>
    public static ImageData? Read(byte[] data, OfficeArtRecord image)
    {
        if (Format(image.Type) is not { } format)
            return null;

        (string extension, bool metafile) = format;
        int after = image.Body + IdentityBytes + (HasSecondIdentity(image.Type, image.Instance) ? IdentityBytes : 0);
        return metafile ? Metafile(data, image, after, extension) : Raster(data, image, after + 1, extension);
    }

    /// <summary>A bitmap, which follows its digests and a one-byte tag with nothing in between.</summary>
    private static ImageData? Raster(byte[] data, OfficeArtRecord image, int start, string extension)
    {
        int count = image.End - start;
        if (start < 0 || count <= 0 || start + count > data.Length)
            return null;

        byte[] bytes = data.AsSpan(start, count).ToArray();
        return extension == "dib" ? Bitmap(bytes) : ImageData.FromBytes(bytes, ImageData.ContentTypeForExtension(extension));
    }

    /// <summary>A metafile, which is deflated unless its header says otherwise.</summary>
    private static ImageData? Metafile(byte[] data, OfficeArtRecord image, int start, string extension)
    {
        if (start < 0 || start + MetafileHeaderBytes > image.End || image.End > data.Length)
            return null;

        int original = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(start));
        int saved = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(start + 28));
        bool deflated = data[start + 32] == 0x00;

        int from = start + MetafileHeaderBytes;
        int available = image.End - from;
        int count = saved > 0 && saved <= available ? saved : available;
        if (count <= 0)
            return null;

        byte[] bytes = deflated ? Inflate(data, from, count, original) : data.AsSpan(from, count).ToArray();
        return bytes.Length == 0 ? null : ImageData.FromBytes(bytes, ImageData.ContentTypeForExtension(extension));
    }

    /// <summary>Expands a deflated metafile, giving up rather than throwing on damaged bytes.</summary>
    private static byte[] Inflate(byte[] data, int start, int count, int original)
    {
        try
        {
            using var source = new MemoryStream(data, start, count, writable: false);
            using var expanding = new ZLibStream(source, CompressionMode.Decompress);
            using var result = new MemoryStream(original is > 0 and <= 64 * 1024 * 1024 ? original : count);
            expanding.CopyTo(result);
            return result.ToArray();
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    /// <summary>
    /// Puts the file header back on a device-independent bitmap. The drawing layer stores
    /// only what a bitmap file has after its first fourteen bytes, so those are rebuilt from
    /// the information block that follows them.
    /// </summary>
    private static ImageData? Bitmap(byte[] dib)
    {
        const int FileHeaderBytes = 14;
        if (dib.Length < 40)
            return null;

        int informationBytes = BinaryPrimitives.ReadInt32LittleEndian(dib);
        ushort depth = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        int colors = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(32));
        if (informationBytes < 40 || informationBytes > dib.Length)
            return null;

        if (colors <= 0 && depth <= 8)
            colors = 1 << depth;

        var file = new byte[FileHeaderBytes + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), FileHeaderBytes + informationBytes + (colors * 4));
        dib.CopyTo(file, FileHeaderBytes);
        return ImageData.FromBytes(file, "image/bmp");
    }

    /// <summary>
    /// The seven BLIP types ([MS-ODRAW] 2.2.23). PICT is stored the way the two metafiles are —
    /// behind an <c>OfficeArtMetafileHeader</c> and usually deflated — because on the Macintosh
    /// it was one (2.2.26).
    /// </summary>
    private static (string Extension, bool IsMetafile)? Format(ushort type) => type switch
    {
        0xF01A => ("emf", true),
        0xF01B => ("wmf", true),
        0xF01C => ("pict", true),
        0xF01D or 0xF02A => ("jpg", false),
        0xF01E => ("png", false),
        0xF01F => ("dib", false),
        0xF029 => ("tiff", false),
        _ => null,
    };

    /// <summary>
    /// An image carries one digest of its pixels, or two when the stored form differs from
    /// the original. The record's instance number is what says which.
    /// </summary>
    private static bool HasSecondIdentity(ushort type, int instance) => type switch
    {
        0xF01A => instance == 0x3D5,
        0xF01B => instance == 0x217,
        0xF01C => instance == 0x543,
        0xF01D or 0xF02A => instance is 0x46B or 0x6E3,
        0xF01E => instance == 0x6E1,
        0xF01F => instance == 0x7A9,
        0xF029 => instance == 0x6E5,
        _ => false,
    };
}
