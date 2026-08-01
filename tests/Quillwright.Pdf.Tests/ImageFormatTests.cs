using System.Text;
using Quillwright.Model;
using Quillwright.Pdf.Images;
using Quillwright.Primitives;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// The formats a PDF cannot carry as they stand, and what the converter makes of them.
/// </summary>
/// <remarks>
/// Each fixture is built here rather than read from disk, and built by writing the format's own
/// layout out by hand: a fixture produced by the same understanding the decoder has could not
/// catch a misreading of the specification, only a change of mind.
/// </remarks>
public sealed class ImageFormatTests
{
    private static (byte Red, byte Green, byte Blue) PixelAt(RasterImage image, int x, int y)
    {
        int at = (((y * image.Width) + x) * 3) + 0;
        return (image.Samples[at], image.Samples[at + 1], image.Samples[at + 2]);
    }

    private static RasterImage Decoded(byte[] file)
    {
        ImageSource source = RasterDecoder.Decode(file);
        Assert.NotNull(source.Pixels);
        return source.Pixels.Value;
    }

    [Fact]
    public void ABitmap_IsDecodedIntoItsPixels()
    {
        RasterImage image = Decoded(Raster.Bmp(5, 3, 0x20, 0x40, 0x60));

        Assert.Equal(5, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal((0x20, 0x40, 0x60), PixelAt(image, 0, 0));
        Assert.Equal((0x20, 0x40, 0x60), PixelAt(image, 4, 2));
        Assert.Null(image.Alpha);
    }

    [Fact]
    public void ABitmapDeclaredTopDown_ReadsTheSameWayUp()
    {
        // The two files store their rows in opposite orders and mean the same picture, which is
        // only visible in a bitmap whose rows differ — so the palette one, split down the middle,
        // is the one worth comparing.
        RasterImage upwards = Decoded(Raster.BmpPalette(4, 2));
        RasterImage downwards = Decoded(Raster.Bmp(4, 2, 0x11, 0x22, 0x33, topDown: true));

        Assert.Equal((0xFF, 0, 0), PixelAt(upwards, 0, 0));
        Assert.Equal((0, 0xFF, 0), PixelAt(upwards, 3, 1));
        Assert.Equal((0x11, 0x22, 0x33), PixelAt(downwards, 0, 0));
    }

    [Fact]
    public void APaletteBitmap_ResolvesItsColourTable()
    {
        RasterImage image = Decoded(Raster.BmpPalette(6, 2));

        Assert.Equal((0xFF, 0, 0), PixelAt(image, 0, 0));
        Assert.Equal((0xFF, 0, 0), PixelAt(image, 2, 1));
        Assert.Equal((0, 0xFF, 0), PixelAt(image, 3, 0));
        Assert.Equal((0, 0xFF, 0), PixelAt(image, 5, 1));
    }

    [Fact]
    public void ARunLengthBitmap_ExpandsToItsRuns()
    {
        RasterImage image = Decoded(Raster.BmpRunLength(4, 3));

        Assert.Equal(4, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal((0xFF, 0, 0), PixelAt(image, 0, 0));
        Assert.Equal((0xFF, 0, 0), PixelAt(image, 3, 2));
    }

    [Fact]
    public void AGif_IsDecodedIntoItsPixels()
    {
        RasterImage image = Decoded(Raster.Gif(8, 4));

        Assert.Equal(8, image.Width);
        Assert.Equal(4, image.Height);
        Assert.Equal((0xFF, 0, 0), PixelAt(image, 0, 0));
        Assert.Equal((0, 0xFF, 0), PixelAt(image, 7, 3));
        Assert.Null(image.Alpha);
    }

    [Fact]
    public void AGifLongerThanOneCodeReset_KeepsReadingAfterIt()
    {
        // The encoder sends a reset before the code width would have to grow, which is the one
        // place a decoder that ignores the dictionary quietly diverges.
        RasterImage image = Decoded(Raster.Gif(40, 20));

        Assert.Equal((0xFF, 0, 0), PixelAt(image, 0, 19));
        Assert.Equal((0, 0xFF, 0), PixelAt(image, 39, 19));
    }

    [Fact]
    public void AGifWithATransparentIndex_CarriesAMask()
    {
        RasterImage image = Decoded(Raster.Gif(8, 4, transparent: 1));

        Assert.NotNull(image.Alpha);
        Assert.Equal(0, image.Alpha![0]);
        Assert.Equal(0xFF, image.Alpha[^1]);
    }

    [Theory]
    [InlineData(Raster.TiffPacking.None)]
    [InlineData(Raster.TiffPacking.PackBits)]
    [InlineData(Raster.TiffPacking.Deflate)]
    public void ATiff_IsDecodedWhicheverWayItIsPacked(Raster.TiffPacking packing)
    {
        RasterImage image = Decoded(Raster.Tiff(6, 4, 0x10, 0x80, 0xF0, packing));

        Assert.Equal(6, image.Width);
        Assert.Equal(4, image.Height);
        Assert.Equal((0x10, 0x80, 0xF0), PixelAt(image, 0, 0));
        Assert.Equal((0x10, 0x80, 0xF0), PixelAt(image, 5, 3));
    }

    [Fact]
    public void AWindowsMetafileWrappingABitmap_GivesUpTheBitmap()
    {
        RasterImage image = Decoded(Raster.Wmf(Raster.Dib24(4, 2, 0x0A, 0x0B, 0x0C)));

        Assert.Equal(4, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal((0x0A, 0x0B, 0x0C), PixelAt(image, 3, 1));
    }

    [Fact]
    public void AnEnhancedMetafileWrappingABitmap_GivesUpTheBitmap()
    {
        RasterImage image = Decoded(Raster.Emf(Raster.Dib24(4, 2, 0x0A, 0x0B, 0x0C)));

        Assert.Equal(4, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal((0x0A, 0x0B, 0x0C), PixelAt(image, 0, 0));
    }

    [Fact]
    public void AMetafileHoldingSeveralBitmaps_KeepsTheLargest()
    {
        byte[] small = Raster.Emf(Raster.Dib24(2, 2, 0xFF, 0, 0));
        byte[] large = Raster.Emf(Raster.Dib24(9, 6, 0, 0, 0xFF));

        // Two files spliced into one: a header, both drawing records, and the end record.
        byte[] joined = [.. small[..^20], .. large[88..]];
        RasterImage image = Decoded(joined);

        Assert.Equal(9, image.Width);
        Assert.Equal((0, 0, 0xFF), PixelAt(image, 0, 0));
    }

    [Fact]
    public void AMetafileThatDrawsRatherThanWraps_IsDeclined()
    {
        // An enhanced metafile with a header, an end record and nothing between them.
        byte[] file = new byte[108];
        file[0] = 1;
        file[4] = 88;
        " EMF"u8.CopyTo(file.AsSpan(40));
        file[88] = 14;
        file[92] = 20;

        Assert.True(RasterDecoder.Decode(file).IsEmpty);
    }

    [Fact]
    public void AMetafileWrappingAJpeg_HandsItOverUntouched()
    {
        // A bitmap header can say its pixels are a whole file of another format; the four bytes
        // here stand in for one, and what matters is that they come back rather than being read
        // as samples.
        byte[] dib = Raster.Dib24(4, 2, 0, 0, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 4);
        dib[40] = 0xFF;
        dib[41] = 0xD8;
        dib[42] = 0xFF;

        ImageSource source = RasterDecoder.Decode(Raster.Emf(dib));

        Assert.Null(source.Pixels);
        Assert.False(source.Encoded.IsEmpty);
        Assert.Equal(0xFF, source.Encoded.Span[0]);
        Assert.Equal(0xD8, source.Encoded.Span[1]);
    }

    [Fact]
    public void SomethingThatIsNoImageAtAll_IsDeclinedRatherThanThrowing()
    {
        Assert.True(RasterDecoder.Decode("not an image at all"u8).IsEmpty);
        Assert.True(RasterDecoder.Decode([]).IsEmpty);
    }

    [Fact]
    public void ABitmapInADocument_IsDrawnAndTheReEncodingIsSaid()
    {
        using Rendered rendered = Rendered.Of(WithPicture(Raster.Bmp(20, 10, 0x30, 0x60, 0x90), "image/bmp"));
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.Contains(" Do", content, StringComparison.Ordinal);
        Assert.Contains(rendered.Diagnostics, warning => warning.Kind == PdfExportWarningKind.ImageTranscoded);
        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Kind == PdfExportWarningKind.ImageSkipped);
    }

    [Fact]
    public void AJpegOrPngInADocument_IsEmbeddedWithoutBeingTouched()
    {
        using Rendered rendered = Rendered.Of(WithPicture(Pixels.Png(20, 10), "image/png"));

        Assert.Empty(rendered.Diagnostics);
    }

    [Fact]
    public void AMetafileThatCannotBeUnwrapped_LeavesTheSpaceBlankAndSaysSo()
    {
        byte[] file = new byte[108];
        file[0] = 1;
        file[4] = 88;
        " EMF"u8.CopyTo(file.AsSpan(40));
        file[88] = 14;
        file[92] = 20;

        using Rendered rendered = Rendered.Of(WithPicture(file, "image/x-emf"));

        Assert.Contains(rendered.Diagnostics, warning => warning.Kind == PdfExportWarningKind.ImageSkipped);
    }

    private static WordDocument WithPicture(byte[] bytes, string contentType)
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Beside a picture.");
        paragraph.AppendPicture(
            ImageData.FromBytes(bytes, contentType),
            Length.FromCentimeters(4),
            Length.FromCentimeters(2));

        return document;
    }
}
