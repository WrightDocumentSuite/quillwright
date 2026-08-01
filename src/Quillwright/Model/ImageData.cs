using System.Buffers.Binary;
using Quillwright.Primitives;

namespace Quillwright.Model;

/// <summary>
/// The bytes of an image plus what a consumer needs to place it: content type, pixel size
/// and resolution. One instance is shared by every picture that displays the same image, so
/// a logo repeated on forty pages is stored once.
/// </summary>
public sealed class ImageData
{
    private ImageData(ReadOnlyMemory<byte> bytes, string contentType, string extension, int pixelWidth, int pixelHeight, double dotsPerInch)
    {
        Bytes = bytes;
        ContentType = contentType;
        Extension = extension;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DotsPerInch = dotsPerInch;
    }

    /// <summary>The encoded image.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>MIME type of the encoding.</summary>
    public string ContentType { get; }

    /// <summary>File extension used for the package part, without the dot.</summary>
    public string Extension { get; }

    /// <summary>Width in pixels, or zero when it could not be determined.</summary>
    public int PixelWidth { get; }

    /// <summary>Height in pixels, or zero when it could not be determined.</summary>
    public int PixelHeight { get; }

    /// <summary>Resolution the natural size is computed from.</summary>
    public double DotsPerInch { get; }

    /// <summary>Package part the image is stored in; assigned when the document is loaded or saved.</summary>
    public string? PartPath { get; internal set; }

    /// <summary>Relationship id the markup refers to the image by.</summary>
    public string? RelationshipId { get; internal set; }

    /// <summary>The size the image is displayed at when nothing else is specified.</summary>
    public Length NaturalWidth => Length.FromPixels(PixelWidth, DotsPerInch);

    /// <summary>The size the image is displayed at when nothing else is specified.</summary>
    public Length NaturalHeight => Length.FromPixels(PixelHeight, DotsPerInch);

    /// <summary>Reads an image from bytes, sniffing the format from its header.</summary>
    /// <param name="bytes">The encoded image.</param>
    /// <param name="contentType">Overrides the sniffed MIME type.</param>
    public static ImageData FromBytes(ReadOnlyMemory<byte> bytes, string? contentType = null)
    {
        (string sniffedType, string extension, int width, int height, double dpi) = Sniff(bytes.Span);

        // A format this does not read the header of — a metafile above all — still belongs in
        // a part named after what it is, so the caller's word is taken when sniffing gave up.
        if (contentType is not null && extension == UnknownExtension)
            extension = ExtensionForContentType(contentType);

        return new ImageData(bytes, contentType ?? sniffedType, extension, width, height, dpi);
    }

    /// <summary>Reads an image from a file.</summary>
    /// <param name="path">Path to the image file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<ImageData> FromFileAsync(string path, CancellationToken cancellationToken = default) =>
        FromBytes(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));

    /// <summary>Reads an image from a stream.</summary>
    /// <param name="stream">Stream positioned at the start of the image.</param>
    /// <param name="contentType">Overrides the sniffed MIME type.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<ImageData> FromStreamAsync(Stream stream, string? contentType = null, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return FromBytes(buffer.ToArray(), contentType);
    }

    /// <summary>Maps a media part extension onto its MIME type.</summary>
    public static string ContentTypeForExtension(string extension) => extension.ToLowerInvariant() switch
    {
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "tif" or "tiff" => "image/tiff",
        "emf" => "image/x-emf",
        "wmf" => "image/x-wmf",
        "pict" or "pct" => "image/pict",
        "svg" => "image/svg+xml",
        "webp" => "image/webp",
        _ => "application/octet-stream",
    };

    /// <summary>Maps a MIME type onto the extension a media part holding it is named with.</summary>
    /// <param name="contentType">MIME type of the encoding.</param>
    public static string ExtensionForContentType(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpeg",
        "image/gif" => "gif",
        "image/bmp" => "bmp",
        "image/tiff" => "tiff",
        "image/x-emf" or "image/emf" => "emf",
        "image/x-wmf" or "image/wmf" => "wmf",

        // ISO/IEC 29500 spells the Macintosh format image/pict; image/x-pict is what some
        // producers write, and both name the same part extension.
        "image/pict" or "image/x-pict" => "pict",
        "image/svg+xml" => "svg",
        "image/webp" => "webp",
        _ => UnknownExtension,
    };

    /// <summary>Extension of a part whose format could not be told from its bytes.</summary>
    private const string UnknownExtension = "bin";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static (string ContentType, string Extension, int Width, int Height, double Dpi) Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes.StartsWith(PngSignature))
            return SniffPng(bytes);

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            return SniffJpeg(bytes);

        if (bytes.Length >= 10 && (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)))
        {
            return ("image/gif", "gif",
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]), 96);
        }

        if (bytes.Length >= 26 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            int pixelsPerMeter = BinaryPrimitives.ReadInt32LittleEndian(bytes[38..]);
            return ("image/bmp", "bmp",
                BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]),
                Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes[22..])),
                pixelsPerMeter > 0 ? pixelsPerMeter * 0.0254 : 96);
        }

        return ("application/octet-stream", UnknownExtension, 0, 0, 96);
    }

    private static (string, string, int, int, double) SniffPng(ReadOnlySpan<byte> bytes)
    {
        int width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..]);
        int height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..]);
        double dpi = 96;

        // Walk the chunk list for pHYs, which carries the resolution in pixels per metre.
        int offset = 8;
        while (offset + 12 <= bytes.Length)
        {
            int chunkLength = BinaryPrimitives.ReadInt32BigEndian(bytes[offset..]);
            if (chunkLength < 0 || offset + 12 + chunkLength > bytes.Length)
                break;

            ReadOnlySpan<byte> type = bytes.Slice(offset + 4, 4);
            if (type.SequenceEqual("pHYs"u8) && chunkLength >= 9 && bytes[offset + 8 + 8] == 1)
            {
                int perMeter = BinaryPrimitives.ReadInt32BigEndian(bytes[(offset + 8)..]);
                if (perMeter > 0)
                    dpi = perMeter * 0.0254;
                break;
            }

            if (type.SequenceEqual("IDAT"u8))
                break;
            offset += 12 + chunkLength;
        }

        return ("image/png", "png", width, height, dpi);
    }

    private static (string, string, int, int, double) SniffJpeg(ReadOnlySpan<byte> bytes)
    {
        int width = 0, height = 0;
        double dpi = 96;
        int offset = 2;

        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            byte marker = bytes[offset + 1];
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                offset += 2;
                continue;
            }

            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 2)..]);
            if (segmentLength < 2 || offset + 2 + segmentLength > bytes.Length)
                break;

            ReadOnlySpan<byte> payload = bytes.Slice(offset + 4, segmentLength - 2);
            if (marker == 0xE0 && payload.Length >= 12 && payload.StartsWith("JFIF\0"u8) && payload[7] == 1)
            {
                ushort density = BinaryPrimitives.ReadUInt16BigEndian(payload[8..]);
                if (density > 0)
                    dpi = density;
            }
            else if (marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7) or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF) &&
                     payload.Length >= 5)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
                break;
            }

            offset += 2 + segmentLength;
        }

        return ("image/jpeg", "jpeg", width, height, dpi);
    }
}
