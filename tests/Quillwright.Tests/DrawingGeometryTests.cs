using Quillwright.Formats;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Tests;

/// <summary>
/// Reading the geometry of a drawing: how big it is, where it sits, what the text does about it.
/// </summary>
/// <remarks>
/// The markup is kept verbatim whatever this makes of it, so nothing here can lose anything. What
/// it can do is leave a renderer guessing, which is why both branches Word writes are read: the
/// modern one for a file Word saved, and the legacy one for a file converted out of <c>.doc</c>,
/// which has no modern branch at all.
/// </remarks>
public class DrawingGeometryTests
{
    private const string Namespaces =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" " +
        "xmlns:w10=\"urn:schemas-microsoft-com:office:word\"";

    private static string Anchored(string position, string wrap, string extra = "") => $"""
        <w:drawing {Namespaces}>
          <wp:anchor {extra}simplePos="0" relativeHeight="2" locked="0" layoutInCell="1" allowOverlap="1">
            <wp:simplePos x="0" y="0"/>
            {position}
            <wp:extent cx="1828800" cy="914400"/>
            {wrap}
            <wp:docPr id="1" name="Picture 1" descr="A kitten"/>
          </wp:anchor>
        </w:drawing>
        """;

    [Fact]
    public void AnOffsetPosition_IsReadAsADistanceFromWhatItNames()
    {
        DrawingGeometry found = DrawingGeometry.Read(Anchored(
            """
            <wp:positionH relativeFrom="page"><wp:posOffset>914400</wp:posOffset></wp:positionH>
            <wp:positionV relativeFrom="paragraph"><wp:posOffset>-228600</wp:posOffset></wp:positionV>
            """,
            "<wp:wrapSquare wrapText=\"bothSides\"/>"));

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(found.Anchor);

        Assert.Equal(AnchorOrigin.Page, anchor.HorizontalFrom);
        Assert.Equal(AnchorOrigin.Paragraph, anchor.VerticalFrom);
        Assert.Equal(Length.FromInches(1), anchor.OffsetX);
        Assert.Equal(Length.FromInches(-0.25), anchor.OffsetY);
        Assert.Equal(AnchorAlignment.Offset, anchor.HorizontalAlignment);
    }

    [Fact]
    public void AnAlignedPosition_IsReadAsAnEdge()
    {
        DrawingGeometry found = DrawingGeometry.Read(Anchored(
            """
            <wp:positionH relativeFrom="margin"><wp:align>center</wp:align></wp:positionH>
            <wp:positionV relativeFrom="page"><wp:align>bottom</wp:align></wp:positionV>
            """,
            "<wp:wrapNone/>"));

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(found.Anchor);

        Assert.Equal(AnchorAlignment.Center, anchor.HorizontalAlignment);
        Assert.Equal(AnchorAlignment.End, anchor.VerticalAlignment);
        Assert.Equal(AnchorOrigin.Margin, anchor.HorizontalFrom);
        Assert.Equal(AnchorOrigin.Page, anchor.VerticalFrom);
    }

    [Theory]
    [InlineData("<wp:wrapNone/>", TextWrapping.None, WrapSides.Both)]
    [InlineData("<wp:wrapTopAndBottom/>", TextWrapping.TopAndBottom, WrapSides.Both)]
    [InlineData("<wp:wrapSquare wrapText=\"left\"/>", TextWrapping.Square, WrapSides.Left)]
    [InlineData("<wp:wrapTight wrapText=\"largest\"/>", TextWrapping.Tight, WrapSides.Largest)]
    [InlineData("<wp:wrapThrough wrapText=\"right\"/>", TextWrapping.Through, WrapSides.Right)]
    public void EachKindOfWrapping_IsRecognised(string wrap, TextWrapping wrapping, WrapSides sides)
    {
        DrawingGeometry found = DrawingGeometry.Read(Anchored(
            "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>", wrap));

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(found.Anchor);

        Assert.Equal(wrapping, anchor.Wrapping);
        Assert.Equal(sides, anchor.Sides);
    }

    [Fact]
    public void APictureBehindTheText_SaysSo()
    {
        DrawingGeometry found = DrawingGeometry.Read(Anchored(
            "<wp:positionH relativeFrom=\"page\"><wp:posOffset>0</wp:posOffset></wp:positionH>",
            "<wp:wrapNone/>",
            extra: "behindDoc=\"1\" "));

        Assert.True(Assert.IsType<PictureAnchor>(found.Anchor).BehindText);
    }

    [Fact]
    public void TheWrapDistances_AreRead()
    {
        DrawingGeometry found = DrawingGeometry.Read(Anchored(
            "<wp:positionH relativeFrom=\"page\"><wp:posOffset>0</wp:posOffset></wp:positionH>",
            "<wp:wrapSquare wrapText=\"bothSides\"/>",
            extra: "distT=\"91440\" distB=\"45720\" distL=\"228600\" distR=\"0\" "));

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(found.Anchor);

        Assert.Equal(Length.FromInches(0.1), anchor.DistanceTop);
        Assert.Equal(Length.FromInches(0.05), anchor.DistanceBottom);
        Assert.Equal(Length.FromInches(0.25), anchor.DistanceLeft);
        Assert.Equal(Length.Zero, anchor.DistanceRight);
    }

    /// <summary>
    /// An anchor that says nothing about its clearances gets the ones Word writes into every
    /// anchor it saves: an eighth of an inch at the sides and nothing above or below.
    /// </summary>
    [Fact]
    public void AnAnchorSilentAboutDistances_GetsWordsUsualOnes()
    {
        DrawingGeometry found = DrawingGeometry.Read(Anchored(
            "<wp:positionH relativeFrom=\"page\"><wp:posOffset>0</wp:posOffset></wp:positionH>",
            "<wp:wrapSquare wrapText=\"bothSides\"/>"));

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(found.Anchor);

        Assert.Equal(Length.FromInches(0.125), anchor.DistanceLeft);
        Assert.Equal(Length.FromInches(0.125), anchor.DistanceRight);
        Assert.Equal(Length.Zero, anchor.DistanceTop);
        Assert.Equal(Length.Zero, anchor.DistanceBottom);
    }

    [Fact]
    public void TheSizeAndTheNameAreRead()
    {
        DrawingGeometry found = DrawingGeometry.Read(Anchored(
            "<wp:positionH relativeFrom=\"page\"><wp:posOffset>0</wp:posOffset></wp:positionH>",
            "<wp:wrapNone/>"));

        Assert.Equal(Length.FromInches(2).Emu, found.Width);
        Assert.Equal(Length.FromInches(1).Emu, found.Height);
        Assert.Equal("Picture 1", found.Name);
        Assert.Equal("A kitten", found.Description);
    }

    [Fact]
    public void APictureInTheTextFlow_HasNoAnchor()
    {
        DrawingGeometry found = DrawingGeometry.Read($"""
            <w:drawing {Namespaces}>
              <wp:inline><wp:extent cx="914400" cy="914400"/></wp:inline>
            </w:drawing>
            """);

        Assert.True(found.IsInline);
        Assert.Null(found.Anchor);
    }

    /// <summary>
    /// A shape converted out of the binary format has only the legacy branch, and everything a
    /// renderer needs is in one style attribute.
    /// </summary>
    [Fact]
    public void TheLegacyBranch_IsReadWhenItIsAllThereIs()
    {
        DrawingGeometry found = DrawingGeometry.Read($"""
            <w:pict {Namespaces}>
              <v:shape id="_x0000_s1026" type="#_x0000_t202"
                 style="position:absolute;margin-left:36pt;margin-top:18pt;width:180pt;height:90pt;
                        z-index:-251658240;mso-position-horizontal-relative:page;
                        mso-position-vertical-relative:margin"
                 fillcolor="#DBE5F1" strokecolor="#4F81BD" strokeweight="1.5pt">
                <w10:wrap type="square" side="left"/>
              </v:shape>
            </w:pict>
            """);

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(found.Anchor);

        Assert.Equal(Length.FromPoints(180).Emu, found.Width);
        Assert.Equal(Length.FromPoints(90).Emu, found.Height);
        Assert.Equal(Length.FromPoints(36), anchor.OffsetX);
        Assert.Equal(Length.FromPoints(18), anchor.OffsetY);
        Assert.Equal(AnchorOrigin.Page, anchor.HorizontalFrom);
        Assert.Equal(AnchorOrigin.Margin, anchor.VerticalFrom);
        Assert.Equal(TextWrapping.Square, anchor.Wrapping);
        Assert.Equal(WrapSides.Left, anchor.Sides);
        Assert.True(anchor.BehindText);
        Assert.Equal(WordColor.FromRgb(0xDBE5F1), found.Fill);
        Assert.Equal(WordColor.FromRgb(0x4F81BD), found.Outline?.Color);
        Assert.Equal(Length.FromPoints(1.5), found.Outline?.Width);
    }

    /// <summary>
    /// Word writes both branches and they say the same thing in different words. The modern one
    /// is the one to believe, or a value it states precisely would be overwritten by the rounded
    /// copy in the fallback.
    /// </summary>
    [Fact]
    public void TheModernBranch_WinsOverTheFallback()
    {
        DrawingGeometry found = DrawingGeometry.Read($"""
            <mc:AlternateContent {Namespaces}
                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
              <mc:Choice Requires="wps">
                <w:drawing>
                  <wp:anchor simplePos="0" relativeHeight="2" locked="0" layoutInCell="1" allowOverlap="1">
                    <wp:positionH relativeFrom="column"><wp:posOffset>457200</wp:posOffset></wp:positionH>
                    <wp:positionV relativeFrom="paragraph"><wp:posOffset>0</wp:posOffset></wp:positionV>
                    <wp:extent cx="1828800" cy="914400"/>
                    <wp:wrapTopAndBottom/>
                  </wp:anchor>
                </w:drawing>
              </mc:Choice>
              <mc:Fallback>
                <w:pict>
                  <v:shape style="position:absolute;margin-left:99pt;width:1pt;height:1pt"
                     fillcolor="#FF0000">
                    <w10:wrap type="none"/>
                  </v:shape>
                </w:pict>
              </mc:Fallback>
            </mc:AlternateContent>
            """);

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(found.Anchor);

        Assert.Equal(Length.FromInches(2).Emu, found.Width);
        Assert.Equal(Length.FromInches(0.5), anchor.OffsetX);
        Assert.Equal(TextWrapping.TopAndBottom, anchor.Wrapping);
        Assert.Null(found.Fill);
    }

    [Fact]
    public void TheWrappingPolygon_IsRead()
    {
        DrawingGeometry found = DrawingGeometry.Read(Anchored(
            "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>",
            """
            <wp:wrapTight wrapText="bothSides">
              <wp:wrapPolygon edited="0">
                <wp:start x="10800" y="0"/>
                <wp:lineTo x="21600" y="10800"/>
                <wp:lineTo x="10800" y="21600"/>
                <wp:lineTo x="0" y="10800"/>
                <wp:lineTo x="10800" y="0"/>
              </wp:wrapPolygon>
            </wp:wrapTight>
            """));

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(found.Anchor);

        Assert.Equal(TextWrapping.Tight, anchor.Wrapping);
        Assert.Equal(5, anchor.Polygon.Count);
        Assert.Equal(new PolygonPoint(10800, 0), anchor.Polygon[0]);
        Assert.Equal(new PolygonPoint(21600, 10800), anchor.Polygon[1]);
        Assert.Equal(new PolygonPoint(0, 10800), anchor.Polygon[3]);
    }

    [Fact]
    public void TheTextFlowOfATurnedBox_IsRead()
    {
        DrawingGeometry found = DrawingGeometry.Read(Anchored(
            "<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>",
            "<wp:wrapNone/>",
            extra: string.Empty) + string.Empty);

        Assert.Equal(Quillwright.Styles.TextDirection.LeftToRightTopToBottom, found.TextFlow);

        DrawingGeometry turned = DrawingGeometry.Read($"""
            <w:drawing {Namespaces}>
              <wp:anchor simplePos="0" relativeHeight="2" locked="0" layoutInCell="1" allowOverlap="1">
                <wp:positionH relativeFrom="column"><wp:posOffset>0</wp:posOffset></wp:positionH>
                <wp:extent cx="914400" cy="1828800"/>
                <wp:wrapNone/>
                <wp:docPr id="1" name="Box 1"/>
                <wps:wsp><wps:bodyPr vert="vert270"/></wps:wsp>
              </wp:anchor>
            </w:drawing>
            """);

        Assert.Equal(Quillwright.Styles.TextDirection.BottomToTopLeftToRight, turned.TextFlow);
    }

    /// <summary>The legacy branch, converted out of a binary file, states the flow in its style.</summary>
    [Fact]
    public void TheLegacyBranch_StatesItsTextFlowInTheStyle()
    {
        DrawingGeometry found = DrawingGeometry.Read($"""
            <w:pict {Namespaces}>
              <v:shape id="_x0000_s2"
                 style="position:absolute;margin-left:36pt;margin-top:18pt;width:30pt;height:120pt;
                        layout-flow:vertical">
                <w10:wrap type="none"/>
              </v:shape>
            </w:pict>
            """);

        Assert.Equal(Quillwright.Styles.TextDirection.TopToBottomRightToLeft, found.TextFlow);
    }

    /// <summary>The legacy branch states its clearances inside the style attribute.</summary>
    [Fact]
    public void TheLegacyBranch_StatesItsWrapDistancesInTheStyle()
    {
        DrawingGeometry found = DrawingGeometry.Read($"""
            <w:pict {Namespaces}>
              <v:shape id="_x0000_s1"
                 style="position:absolute;margin-left:36pt;margin-top:18pt;width:90pt;height:45pt;
                        mso-wrap-distance-left:18pt;mso-wrap-distance-right:0;
                        mso-wrap-distance-top:6pt;mso-wrap-distance-bottom:12pt">
                <w10:wrap type="square"/>
              </v:shape>
            </w:pict>
            """);

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(found.Anchor);

        Assert.Equal(Length.FromPoints(18), anchor.DistanceLeft);
        Assert.Equal(Length.Zero, anchor.DistanceRight);
        Assert.Equal(Length.FromPoints(6), anchor.DistanceTop);
        Assert.Equal(Length.FromPoints(12), anchor.DistanceBottom);
    }

    /// <summary>An origin one axis has no name for falls back to the one it does.</summary>
    [Fact]
    public void AnOriginFromTheOtherAxis_IsNotBelieved()
    {
        DrawingGeometry found = DrawingGeometry.Read(Anchored(
            """
            <wp:positionH relativeFrom="nonsense"><wp:posOffset>0</wp:posOffset></wp:positionH>
            <wp:positionV relativeFrom="nonsense"><wp:posOffset>0</wp:posOffset></wp:positionV>
            """,
            "<wp:wrapNone/>"));

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(found.Anchor);

        Assert.Equal(AnchorOrigin.Column, anchor.HorizontalFrom);
        Assert.Equal(AnchorOrigin.Paragraph, anchor.VerticalFrom);
    }
}
