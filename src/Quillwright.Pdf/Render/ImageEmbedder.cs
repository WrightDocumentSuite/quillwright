using Inkwright.Images;
using Quillwright.Model;
using Quillwright.Pdf.Images;

namespace Quillwright.Pdf.Render;

/// <summary>
/// Puts the images a document uses into the PDF, once each however often they are drawn.
/// </summary>
/// <remarks>
/// Inkwright embeds JPEG and PNG without re-encoding them, which is the whole point: a photograph
/// keeps its compression and a screenshot keeps its palette. Anything else — a bitmap, a GIF, a
/// TIFF, or a metafile wrapped round one of those — is decoded into samples and written back out
/// deflated, which is lossless but no longer the bytes the document held; that is worth saying,
/// so it is said in the diagnostics. A metafile that draws rather than wraps has no bitmap to
/// take out, and its space is left blank rather than filled with something wrong.
/// </remarks>
internal sealed class ImageEmbedder
{
    private readonly PdfExportContext _context;
    private readonly Dictionary<ImageData, PdfImage?> _cache = [];

    internal ImageEmbedder(PdfExportContext context) => _context = context;

    /// <summary>The embedded image, or <see langword="null"/> when the format cannot be embedded.</summary>
    /// <param name="image">The image the document carries.</param>
    public PdfImage? Embed(ImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (_cache.TryGetValue(image, out PdfImage? cached))
            return cached;

        PdfImage? embedded = TryEmbed(image);
        _cache[image] = embedded;
        return embedded;
    }

    private PdfImage? TryEmbed(ImageData image)
    {
        try
        {
            return PdfImage.Load(_context.Pdf, image.Bytes.Span);
        }
        catch (NotSupportedException)
        {
            return Transcode(image);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            _context.Diagnostics.Add(
                PdfExportWarningKind.ImageSkipped,
                $"An image of type '{image.ContentType}' could not be read: {exception.Message}",
                image.PartPath ?? image.ContentType);
            return null;
        }
    }

    /// <summary>Decodes a format the PDF writer will not take, and re-encodes what comes out.</summary>
    private PdfImage? Transcode(ImageData image)
    {
        ImageSource source = RasterDecoder.Decode(image.Bytes.Span);
        if (source.IsEmpty)
        {
            _context.Diagnostics.Add(
                PdfExportWarningKind.ImageSkipped,
                $"An image of type '{image.ContentType}' holds no bitmap this converter can draw.",
                image.PartPath ?? image.ContentType);
            return null;
        }

        try
        {
            PdfImage embedded = source.Pixels is { } pixels
                ? pixels.ToPdf(_context.Pdf)
                : PdfImage.Load(_context.Pdf, source.Encoded.Span);

            _context.Diagnostics.Add(
                PdfExportWarningKind.ImageTranscoded,
                $"An image of type '{image.ContentType}' was decoded and re-encoded to travel in a PDF.",
                image.PartPath ?? image.ContentType);

            return embedded;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidDataException or ArgumentException)
        {
            _context.Diagnostics.Add(
                PdfExportWarningKind.ImageSkipped,
                $"An image of type '{image.ContentType}' could not be re-encoded: {exception.Message}",
                image.PartPath ?? image.ContentType);
            return null;
        }
    }
}
