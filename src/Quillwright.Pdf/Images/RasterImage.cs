using Inkwright;
using Inkwright.Images;

namespace Quillwright.Pdf.Images;

/// <summary>
/// An image taken apart into the samples a PDF image XObject is made of: eight bits a channel,
/// interleaved, the top row first.
/// </summary>
/// <param name="Width">Width in samples.</param>
/// <param name="Height">Height in samples.</param>
/// <param name="Samples">Interleaved samples, <paramref name="Components"/> to a pixel.</param>
/// <param name="Components">One for grey, three for colour.</param>
/// <param name="Alpha">One byte a pixel, or <see langword="null"/> when the image is opaque.</param>
internal readonly record struct RasterImage(int Width, int Height, byte[] Samples, int Components, byte[]? Alpha)
{
    /// <summary>Adds the samples to a document as an image XObject.</summary>
    /// <param name="document">The document being written.</param>
    public PdfImage ToPdf(PdfDocument document) =>
        PdfImage.FromPixels(document, Width, Height, Samples, Components, Alpha ?? []);
}

/// <summary>
/// What was found inside an image the PDF writer will not take as it stands: either samples to
/// re-encode, or a JPEG or PNG that was wrapped in a container and can go straight through.
/// </summary>
/// <param name="Pixels">The decoded samples, when the image had to be taken apart.</param>
/// <param name="Encoded">A JPEG or PNG file found inside a container, embedded untouched.</param>
internal readonly record struct ImageSource(RasterImage? Pixels, ReadOnlyMemory<byte> Encoded)
{
    /// <summary>Nothing usable was found.</summary>
    public static ImageSource None => default;

    /// <summary>Whether anything was found at all.</summary>
    public bool IsEmpty => Pixels is null && Encoded.IsEmpty;

    /// <summary>Samples that have to be re-encoded to travel in a PDF.</summary>
    /// <param name="image">The decoded image.</param>
    public static ImageSource FromPixels(RasterImage image) => new(image, default);

    /// <summary>A file a PDF can carry unchanged.</summary>
    /// <param name="bytes">The JPEG or PNG.</param>
    public static ImageSource FromEncoded(ReadOnlyMemory<byte> bytes) => new(null, bytes);
}
