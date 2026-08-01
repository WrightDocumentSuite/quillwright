using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Tests;

/// <summary>
/// Where a floating picture sits and what the text does about it (<c>wp:anchor</c>,
/// ISO/IEC 29500-1 §20.4.2.3). Every combination has to come out as markup a reader accepts,
/// because the kinds of wrapping are separate elements rather than one attribute and two of
/// them are not allowed to stand alone.
/// </summary>
public class PictureAnchorTests
{
    [Theory]
    [InlineData(TextWrapping.Square, "<wp:wrapSquare wrapText=\"bothSides\"/>")]
    [InlineData(TextWrapping.TopAndBottom, "<wp:wrapTopAndBottom/>")]
    [InlineData(TextWrapping.None, "<wp:wrapNone/>")]
    public async Task EachKindOfWrapping_IsItsOwnElement(TextWrapping wrapping, string expected)
    {
        string markup = await MarkupAsync(new PictureAnchor { Wrapping = wrapping });

        Assert.Contains(expected, markup, StringComparison.Ordinal);
    }

    /// <summary>Wrapping that follows an outline has to say what the outline is, or the file is invalid.</summary>
    [Theory]
    [InlineData(TextWrapping.Tight, "wrapTight")]
    [InlineData(TextWrapping.Through, "wrapThrough")]
    public async Task WrappingThatFollowsAnOutline_CarriesOne(TextWrapping wrapping, string element)
    {
        string markup = await MarkupAsync(new PictureAnchor { Wrapping = wrapping, Sides = WrapSides.Largest });

        Assert.Contains($"<wp:{element} wrapText=\"largest\">", markup, StringComparison.Ordinal);
        Assert.Contains("<wp:wrapPolygon edited=\"0\">", markup, StringComparison.Ordinal);
        Assert.Contains($"</wp:{element}>", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOffsetPosition_IsWrittenAsADistance()
    {
        string markup = await MarkupAsync(new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Page,
            VerticalFrom = AnchorOrigin.Line,
            OffsetX = Length.FromTwips(1000),
            OffsetY = Length.FromTwips(-200),
        });

        Assert.Contains("<wp:positionH relativeFrom=\"page\"><wp:posOffset>635000</wp:posOffset>", markup, StringComparison.Ordinal);
        Assert.Contains("<wp:positionV relativeFrom=\"line\"><wp:posOffset>-127000</wp:posOffset>", markup, StringComparison.Ordinal);
    }

    /// <summary>A position is either a distance or an edge to line up with, never both.</summary>
    [Fact]
    public async Task AnAlignedPosition_IsWrittenAsAnEdgeInstead()
    {
        string markup = await MarkupAsync(new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Margin,
            HorizontalAlignment = AnchorAlignment.Center,
            VerticalAlignment = AnchorAlignment.End,
            OffsetX = Length.FromTwips(1000),
        });

        Assert.Contains("<wp:positionH relativeFrom=\"margin\"><wp:align>center</wp:align>", markup, StringComparison.Ordinal);
        Assert.Contains("<wp:align>bottom</wp:align>", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("posOffset", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APictureBehindTheText_SaysSo()
    {
        string markup = await MarkupAsync(new PictureAnchor { Wrapping = TextWrapping.None, BehindText = true });

        Assert.Contains("behindDoc=\"1\"", markup, StringComparison.Ordinal);
    }

    /// <summary>An origin that means nothing on this axis falls back to the one that does.</summary>
    [Fact]
    public async Task AnOriginFromTheOtherAxis_IsNotWritten()
    {
        string markup = await MarkupAsync(new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Paragraph,
            VerticalFrom = AnchorOrigin.Column,
        });

        Assert.Contains("<wp:positionH relativeFrom=\"column\">", markup, StringComparison.Ordinal);
        Assert.Contains("<wp:positionV relativeFrom=\"paragraph\">", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFloatingPictureThatSaysNothing_IsAnchoredWhereItsParagraphIs()
    {
        string markup = await MarkupAsync(anchor: null);

        Assert.Contains("<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset>", markup, StringComparison.Ordinal);
        Assert.Contains("<wp:wrapSquare wrapText=\"bothSides\"/>", markup, StringComparison.Ordinal);
        Assert.Contains("behindDoc=\"0\"", markup, StringComparison.Ordinal);
    }

    /// <summary>The anchor is model state, so it has to come back when the file is read again.</summary>
    [Fact]
    public async Task AnAnchor_SurvivesARoundTrip()
    {
        WordDocument document = Document(new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Page,
            OffsetX = Length.FromCentimeters(3),
            VerticalFrom = AnchorOrigin.Margin,
            VerticalAlignment = AnchorAlignment.Center,
            Wrapping = TextWrapping.TopAndBottom,
            BehindText = true,
            DistanceTop = Length.FromInches(0.1),
            DistanceBottom = Length.FromInches(0.2),
            DistanceLeft = Length.FromInches(0.25),
            DistanceRight = Length.Zero,
        });

        Picture picture = await ReopenAsync(document);

        Assert.False(picture.IsInline);
        PictureAnchor anchor = Assert.IsType<PictureAnchor>(picture.Anchor);

        Assert.Equal(AnchorOrigin.Page, anchor.HorizontalFrom);
        Assert.Equal(Length.FromCentimeters(3), anchor.OffsetX);
        Assert.Equal(AnchorOrigin.Margin, anchor.VerticalFrom);
        Assert.Equal(AnchorAlignment.Center, anchor.VerticalAlignment);
        Assert.Equal(TextWrapping.TopAndBottom, anchor.Wrapping);
        Assert.True(anchor.BehindText);
        Assert.Equal(Length.FromInches(0.1), anchor.DistanceTop);
        Assert.Equal(Length.FromInches(0.2), anchor.DistanceBottom);
        Assert.Equal(Length.FromInches(0.25), anchor.DistanceLeft);
        Assert.Equal(Length.Zero, anchor.DistanceRight);
    }

    /// <summary>The polygon an outline-wrapped picture carries has to come back point for point.</summary>
    [Fact]
    public async Task AWrappingPolygon_SurvivesARoundTrip()
    {
        WordDocument document = Document(new PictureAnchor
        {
            Wrapping = TextWrapping.Tight,
            Polygon =
            [
                new PolygonPoint(10800, 0),
                new PolygonPoint(21600, 10800),
                new PolygonPoint(10800, 21600),
                new PolygonPoint(0, 10800),
            ],
        });

        Picture picture = await ReopenAsync(document);
        PictureAnchor anchor = Assert.IsType<PictureAnchor>(picture.Anchor);

        Assert.Equal(TextWrapping.Tight, anchor.Wrapping);
        Assert.Equal(4, anchor.Polygon.Count);
        Assert.Equal(new PolygonPoint(21600, 10800), anchor.Polygon[1]);
        Assert.Equal(new PolygonPoint(0, 10800), anchor.Polygon[3]);
    }

    /// <summary>A picture that flows with the text has no anchor to come back.</summary>
    [Fact]
    public async Task APictureInTheTextFlow_ComesBackWithoutOne()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("beside a picture").AppendPicture(
            ImageData.FromBytes(TestImages.Png), Length.FromCentimeters(3), Length.FromCentimeters(2));

        Picture picture = await ReopenAsync(document);

        Assert.True(picture.IsInline);
        Assert.Null(picture.Anchor);
    }

    /// <summary>
    /// Reading the anchor must not count as changing the picture, or every open and save would
    /// rewrite the drawing instead of copying it.
    /// </summary>
    /// <remarks>
    /// The comparison starts at the second save rather than the first: markup generated inside a
    /// document leans on the namespaces its ancestors declared, and preserving it captures those
    /// declarations onto the fragment itself. So the first two saves differ however clean the
    /// picture is, and what proves it clean is that they stop differing after that.
    /// </remarks>
    [Fact]
    public async Task ReadingAnAnchor_DoesNotDirtyThePicture()
    {
        WordDocument document = Document(new PictureAnchor { HorizontalFrom = AnchorOrigin.Page });

        using MemoryStream first = await DocumentFixture.SaveAsync(document);
        using MemoryStream second = await ResaveAsync(first);
        using MemoryStream third = await ResaveAsync(second);

        Assert.Equal(
            OpenXmlAssert.ReadPart(second, "word/document.xml"),
            OpenXmlAssert.ReadPart(third, "word/document.xml"));
    }

    private static async Task<MemoryStream> ResaveAsync(MemoryStream saved)
    {
        saved.Position = 0;
        WordDocument reopened = await WordDocument.LoadAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        return await DocumentFixture.SaveAsync(reopened);
    }

    private static async Task<Picture> ReopenAsync(WordDocument document)
    {
        WordDocument reopened = await DocumentFixture.RoundTripAsync(document, "an anchored picture");
        return reopened.Sections[0].Blocks.Paragraphs
            .SelectMany(static p => p.Objects)
            .Select(static a => a.Object)
            .OfType<Picture>()
            .Single();
    }

    private static WordDocument Document(PictureAnchor? anchor)
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("beside a picture");
        Picture picture = paragraph.AppendPicture(
            ImageData.FromBytes(TestImages.Png), Length.FromCentimeters(3), Length.FromCentimeters(2));

        picture.IsInline = false;
        picture.Anchor = anchor;
        return document;
    }

    private static async Task<string> MarkupAsync(PictureAnchor? anchor)
    {
        using MemoryStream saved = await DocumentFixture.SaveAsync(Document(anchor));
        OpenXmlAssert.Valid(saved, "an anchored picture");
        return OpenXmlAssert.ReadPart(saved, "word/document.xml");
    }
}
