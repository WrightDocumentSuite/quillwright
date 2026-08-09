using System.Globalization;
using System.Xml;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>
/// Everything one walk of a drawing found: how big it is, where it sits, what the text does
/// about it and what it is painted in.
/// </summary>
/// <remarks>
/// <para>
/// The markup is kept verbatim either way. Reading it only adds a typed view, so that a caller
/// can resize a picture, ask where a text box is, or draw either of them; anything the model
/// does not carry is written back as the bytes it arrived as.
/// </para>
/// <para>
/// Word writes the same drawing twice — once in the modern branch and once as the VML an older
/// reader falls back to — so both are read, the modern one wins, and a document converted out
/// of the legacy format, which has only the VML, is still understood.
/// </para>
/// </remarks>
internal sealed partial class DrawingGeometry
{
    /// <summary>Relationship id of the image part, when the drawing shows one.</summary>
    public string? RelationshipId { get; private set; }

    /// <summary>Relationship id of the chart part, when the drawing reserves room for one.</summary>
    public string? ChartRelationshipId { get; private set; }

    /// <summary>Width in English Metric Units, or zero when the markup does not say.</summary>
    public long Width { get; private set; }

    /// <summary>Height in English Metric Units, or zero when the markup does not say.</summary>
    public long Height { get; private set; }

    /// <summary>The name shown in the selection pane.</summary>
    public string? Name { get; private set; }

    /// <summary>The alternative text.</summary>
    public string? Description { get; private set; }

    /// <summary>Whether the drawing flows with the text.</summary>
    public bool IsInline { get; private set; }

    /// <summary>Whether the drawing states a floating position.</summary>
    public bool IsAnchored { get; private set; }

    /// <summary>The background of the shape, or <see langword="null"/> when it has none.</summary>
    public WordColor? Fill { get; private set; }

    /// <summary>The outline of the shape, or <see langword="null"/> when it has none.</summary>
    public BorderLine? Outline => _outlineColor is { } color
        ? BorderLine.Single(_outlineWidth > 0 ? Length.FromEmu(_outlineWidth) : Length.FromPoints(1), color)
        : null;

    /// <summary>Whether the drawing holds words of its own, which makes it a text box.</summary>
    public bool HasText { get; private set; }

    /// <summary>Whether the drawing is a straight line or connector.</summary>
    public bool IsLine { get; private set; }

    /// <summary>Which way the words inside flow (<c>wps:bodyPr@vert</c>, VML <c>layout-flow</c>).</summary>
    public Styles.TextDirection TextFlow { get; private set; }

    /// <summary>Text inset at the left edge; Word's 7.2 point default when omitted.</summary>
    public Length TextInsetLeft { get; private set; } = Length.FromPoints(7.2);

    /// <summary>Text inset at the right edge; Word's 7.2 point default when omitted.</summary>
    public Length TextInsetRight { get; private set; } = Length.FromPoints(7.2);

    /// <summary>Text inset at the top edge; Word's 3.6 point default when omitted.</summary>
    public Length TextInsetTop { get; private set; } = Length.FromPoints(3.6);

    /// <summary>Text inset at the bottom edge; Word's 3.6 point default when omitted.</summary>
    public Length TextInsetBottom { get; private set; } = Length.FromPoints(3.6);

    /// <summary>
    /// Whether the drawing is one image and nothing else. Word writes an image either as a
    /// picture of its own or as the fill of a single shape, and both are the same thing to a
    /// reader; a shape that also holds text is a text box and is left alone.
    /// </summary>
    public bool ShowsOnePicture =>
        (_pictures == 1 && _shapes == 0) ||
        (_pictures == 0 && _shapes == 1 && _filledShapes == 1 && !HasText);

    /// <summary>
    /// Where the drawing sits and what the text does about it, or <see langword="null"/> when it
    /// flows with the text and so needs none of that.
    /// </summary>
    public PictureAnchor? Anchor => IsAnchored
        ? new PictureAnchor
        {
            HorizontalFrom = _horizontalFrom,
            VerticalFrom = _verticalFrom,
            HorizontalAlignment = _horizontalAlignment,
            VerticalAlignment = _verticalAlignment,
            OffsetX = Length.FromEmu(_offsetX),
            OffsetY = Length.FromEmu(_offsetY),
            Wrapping = _wrapping,
            Sides = _sides,
            Polygon = _polygon ?? [],
            BehindText = _behindText,
            DistanceTop = Length.FromEmu(_distanceTop),
            DistanceBottom = Length.FromEmu(_distanceBottom),
            DistanceLeft = Length.FromEmu(_distanceLeft),
            DistanceRight = Length.FromEmu(_distanceRight),
        }
        : null;

    /// <summary>Reads a drawing.</summary>
    /// <param name="markup">The whole element: a drawing, a VML picture, or a compatibility block.</param>
    public static DrawingGeometry Read(string markup)
    {
        var found = new DrawingGeometry();
        var scan = new Scanner(found);

        using var xml = XmlReader.Create(new StringReader(markup), Xml.XmlDefaults.ReaderSettings);
        while (xml.Read())
            scan.Step(xml);

        return found;
    }

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0;

    private static WordColor? ParseColor(string? value) =>
        value is { Length: > 0 } && !value.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? WordColor.Parse(value)
            : null;

    /// <summary>What an offset is measured from, as the two axes name it.</summary>
    private static AnchorOrigin ParseOrigin(string? value, bool horizontal) => value switch
    {
        "page" => AnchorOrigin.Page,
        "margin" => AnchorOrigin.Margin,
        "column" => AnchorOrigin.Column,
        "character" => AnchorOrigin.Character,
        "paragraph" => AnchorOrigin.Paragraph,
        "line" => AnchorOrigin.Line,

        // The margins of one edge are still that edge; nothing finer is modelled.
        "leftMargin" or "insideMargin" => horizontal ? AnchorOrigin.Margin : AnchorOrigin.Margin,
        "rightMargin" or "outsideMargin" => AnchorOrigin.Margin,
        "topMargin" or "bottomMargin" => AnchorOrigin.Margin,
        _ => horizontal ? AnchorOrigin.Column : AnchorOrigin.Paragraph,
    };

    /// <summary>The edge a drawing lines up with, or an offset when it names none.</summary>
    private static AnchorAlignment ParseAlignment(string? value) => value?.Trim() switch
    {
        "left" or "top" => AnchorAlignment.Start,
        "center" or "centre" => AnchorAlignment.Center,
        "right" or "bottom" => AnchorAlignment.End,
        "inside" => AnchorAlignment.Inside,
        "outside" => AnchorAlignment.Outside,
        _ => AnchorAlignment.Offset,
    };

    private static WrapSides ParseSides(string? value) => value switch
    {
        "left" => WrapSides.Left,
        "right" => WrapSides.Right,
        "largest" => WrapSides.Largest,
        _ => WrapSides.Both,
    };
}
