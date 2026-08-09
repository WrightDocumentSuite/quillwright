namespace Quillwright.Model;

/// <summary>
/// The content of a text box: paragraphs and tables, like any other container.
/// </summary>
/// <remarks>
/// A text box is a shape with words in it, and the words are ordinary WordprocessingML held in
/// a <c>w:txbxContent</c> element. Modelling them as a container is what puts them within
/// reach of <c>GetText</c>, find-and-replace and the templating engine; the shape around them is
/// kept verbatim, and what <see cref="Shape"/> says about its size and position is a reading of
/// those same bytes rather than a second copy that could disagree with them.
/// </remarks>
public sealed class TextBox : BlockContainer
{
    /// <inheritdoc />
    public override WordDocument? Document => Owner?.Host?.Document;

    /// <summary>The shape this is the content of.</summary>
    internal Shape? Owner { get; set; }
}

/// <summary>
/// A shape anchored in the text whose words this version reads: a text box, a callout, a
/// banner with a caption in it.
/// </summary>
/// <remarks>
/// <para>
/// The shape itself is kept as the bytes it arrived as, cut into the pieces that surround its
/// content. Only the content is regenerated, so everything else — the effects, the compatibility
/// branch an older reader falls back to, every attribute nobody here has heard of — survives a
/// round trip exactly.
/// </para>
/// <para>
/// Its size, position, fill and outline are readable but not settable, and that is the point:
/// they are a reading of the markup rather than a second copy of it, so a renderer can draw the
/// shape where it belongs while the bytes written back stay the bytes that were read.
/// </para>
/// <para>
/// Word writes a text box twice, once as a modern drawing and once as a VML picture, and both
/// copies hold the same words. When they arrive identical they are written back identical, so
/// editing the text does not leave the fallback saying something else.
/// </para>
/// </remarks>
public sealed class Shape : InlineObject
{
    private static int _generatedDrawingId;

    /// <summary>Creates a shape around content that has already been read.</summary>
    /// <param name="fragments">
    /// The verbatim markup, cut at the places the content goes: one piece more than there are
    /// places, so the content is written between consecutive pieces. A primitive without text
    /// has one whole fragment and no insertion point.
    /// </param>
    /// <param name="content">The content of the shape.</param>
    public Shape(IReadOnlyList<string> fragments, TextBox content)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        ArgumentNullException.ThrowIfNull(content);
        if (fragments.Count < 1)
            throw new ArgumentException("A shape needs preserved markup.", nameof(fragments));

        Fragments = fragments;
        Content = content;
        content.Owner = this;
    }

    /// <summary>Creates an editable floating text box at an explicit page position.</summary>
    /// <param name="width">Box width.</param>
    /// <param name="height">Box height.</param>
    /// <param name="content">Editable paragraphs and tables inside the box.</param>
    /// <param name="anchor">Absolute placement and wrapping.</param>
    public static Shape CreateTextBox(
        Primitives.Length width,
        Primitives.Length height,
        TextBox content,
        PictureAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(anchor);

        long cx = Math.Max(1, Math.Abs(width.Emu));
        long cy = Math.Max(1, Math.Abs(height.Emu));
        string frame = OpenDrawing(cx, cy, anchor, "Fixed-layout text") +
            $"<wps:wsp><wps:cNvSpPr/><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>" +
            "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></wps:spPr>" +
            "<wps:txbx><w:txbxContent>";
        const string close =
            "</w:txbxContent></wps:txbx><wps:bodyPr lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\" wrap=\"none\" anchor=\"t\"/>" +
            "</wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing>";

        return new Shape([frame, close], content)
        {
            Width = width,
            Height = height,
            IsInline = false,
            Anchor = anchor,
            InsetLeft = Primitives.Length.Zero,
            InsetRight = Primitives.Length.Zero,
            InsetTop = Primitives.Length.Zero,
            InsetBottom = Primitives.Length.Zero,
        };
    }

    /// <summary>Creates a floating straight connector at an explicit page position.</summary>
    /// <param name="width">Horizontal extent.</param>
    /// <param name="height">Vertical extent.</param>
    /// <param name="outline">Stroke appearance.</param>
    /// <param name="anchor">Absolute placement and wrapping.</param>
    public static Shape CreateLine(
        Primitives.Length width,
        Primitives.Length height,
        Styles.BorderLine outline,
        PictureAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(anchor);

        long cx = Math.Max(1, Math.Abs(width.Emu));
        long cy = Math.Max(1, Math.Abs(height.Emu));
        long stroke = Math.Max(1, outline.Width.Emu);
        string color = outline.Color.IsAuto ? "000000" : outline.Color.ToHex();
        string markup = OpenDrawing(cx, cy, anchor, "Fixed-layout line") +
            $"<wps:wsp><wps:cNvCnPr><a:cxnSpLocks noChangeShapeType=\"1\"/></wps:cNvCnPr><wps:spPr bwMode=\"auto\"><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>" +
            "<a:prstGeom prst=\"straightConnector1\"><a:avLst/></a:prstGeom><a:noFill/>" +
            $"<a:ln w=\"{stroke}\"><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill><a:round/><a:headEnd/><a:tailEnd/></a:ln></wps:spPr>" +
            "<wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing>";

        return new Shape([markup], new TextBox())
        {
            Width = width,
            Height = height,
            IsInline = false,
            Anchor = anchor,
            Outline = outline,
            IsLine = true,
        };
    }

    private static string OpenDrawing(long cx, long cy, PictureAnchor anchor, string name)
    {
        int id = Interlocked.Increment(ref _generatedDrawingId);
        string horizontal = Origin(anchor.HorizontalFrom, horizontal: true);
        string vertical = Origin(anchor.VerticalFrom, horizontal: false);
        string behind = anchor.BehindText ? "1" : "0";

        return FormattableString.Invariant($"""
            <w:drawing><wp:anchor distT="{anchor.DistanceTop.Emu}" distB="{anchor.DistanceBottom.Emu}" distL="{anchor.DistanceLeft.Emu}" distR="{anchor.DistanceRight.Emu}" simplePos="0" relativeHeight="251658240" behindDoc="{behind}" locked="0" layoutInCell="1" allowOverlap="1"><wp:simplePos x="0" y="0"/><wp:positionH relativeFrom="{horizontal}"><wp:posOffset>{anchor.OffsetX.Emu}</wp:posOffset></wp:positionH><wp:positionV relativeFrom="{vertical}"><wp:posOffset>{anchor.OffsetY.Emu}</wp:posOffset></wp:positionV><wp:extent cx="{cx}" cy="{cy}"/><wp:effectExtent l="0" t="0" r="0" b="0"/><wp:wrapNone/><wp:docPr id="{id}" name="{name} {id}"/><wp:cNvGraphicFramePr/><a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
            """).Trim();
    }

    private static string Origin(AnchorOrigin origin, bool horizontal) => origin switch
    {
        AnchorOrigin.Page => "page",
        AnchorOrigin.Margin => "margin",
        AnchorOrigin.Column when horizontal => "column",
        AnchorOrigin.Character when horizontal => "character",
        AnchorOrigin.Line when !horizontal => "line",
        _ => horizontal ? "column" : "paragraph",
    };

    /// <summary>The verbatim markup around the content.</summary>
    public IReadOnlyList<string> Fragments { get; }

    /// <summary>The words inside the shape.</summary>
    public TextBox Content { get; }

    /// <summary>How wide the shape is drawn, or zero when its markup does not say.</summary>
    public Primitives.Length Width { get; internal init; }

    /// <summary>How tall the shape is drawn, or zero when its markup does not say.</summary>
    public Primitives.Length Height { get; internal init; }

    /// <summary>Whether the shape flows with the text rather than floating.</summary>
    public bool IsInline { get; internal init; } = true;

    /// <summary>
    /// Where a floating shape sits and how the text behaves around it, or <see langword="null"/>
    /// when it flows with the text or its markup says nothing.
    /// </summary>
    public PictureAnchor? Anchor { get; internal init; }

    /// <summary>The background of the shape, or <see langword="null"/> when it has none.</summary>
    public Primitives.WordColor? Fill { get; internal init; }

    /// <summary>Which way the words inside flow: the ordinary way, or down a rotated box.</summary>
    public Styles.TextDirection Direction { get; internal init; }

    /// <summary>Space between the left frame and its text.</summary>
    public Primitives.Length InsetLeft { get; internal init; } = Primitives.Length.FromPoints(7.2);

    /// <summary>Space between the right frame and its text.</summary>
    public Primitives.Length InsetRight { get; internal init; } = Primitives.Length.FromPoints(7.2);

    /// <summary>Space between the top frame and its text.</summary>
    public Primitives.Length InsetTop { get; internal init; } = Primitives.Length.FromPoints(3.6);

    /// <summary>Space between the bottom frame and its text.</summary>
    public Primitives.Length InsetBottom { get; internal init; } = Primitives.Length.FromPoints(3.6);

    /// <summary>The line around the shape, or <see langword="null"/> when it has none.</summary>
    public Styles.BorderLine? Outline { get; internal init; }

    /// <summary>Whether this shape is a straight connector rather than a framed text box.</summary>
    public bool IsLine { get; internal init; }

    /// <summary>The paragraph the shape sits in, once it has been placed.</summary>
    internal Paragraph? Host { get; set; }

    /// <inheritdoc />
    public override string? GetText() => Content.GetText();
}
