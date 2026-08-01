namespace Quillwright.Pdf.Images;

/// <summary>
/// Decodes the image formats a document uses that a PDF cannot carry as they stand, choosing the
/// decoder by what the bytes say rather than by what the package called the part.
/// </summary>
/// <remarks>
/// JPEG and PNG never reach here: they travel into a PDF untouched, which is the whole reason
/// they are handled a layer above. What is left is the three raster formats Word will happily
/// store — bitmap, GIF and TIFF — and the two metafiles, which are unwrapped rather than drawn.
/// </remarks>
internal static class RasterDecoder
{
    /// <summary>Decodes an image, or gives back nothing when it is one this cannot read.</summary>
    /// <param name="data">The encoded image.</param>
    public static ImageSource Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            return ImageSource.None;

        try
        {
            return Dispatch(data);
        }
        catch (Exception error) when (error is IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            // A malformed image is one blank space on a page, not a failed render. Which image
            // it was is named in the diagnostics by the caller.
            return ImageSource.None;
        }
    }

    /// <summary>What each format's name for itself says to do with it.</summary>
    private static ImageSource Dispatch(ReadOnlySpan<byte> data)
    {
        if (data[0] == (byte)'B' && data[1] == (byte)'M')
            return Bitmap(data);

        if (GifReader.Matches(data))
            return GifReader.Read(data);

        if (TiffReader.Matches(data))
            return TiffReader.Read(data);

        return MetafileReader.IsEmf(data) || MetafileReader.IsWmf(data)
            ? MetafileReader.Read(data)
            : ImageSource.None;
    }

    /// <summary>
    /// A bitmap file is a fourteen-byte wrapper saying where the pixels are, around the bitmap
    /// that a metafile would carry on its own.
    /// </summary>
    private static ImageSource Bitmap(ReadOnlySpan<byte> data)
    {
        const int FileHeaderSize = 14;
        if (data.Length <= FileHeaderSize)
            return ImageSource.None;

        int pixels = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        int offset = pixels > FileHeaderSize && pixels < data.Length ? pixels - FileHeaderSize : -1;
        return DibReader.Read(data[FileHeaderSize..], offset);
    }
}
