using System.Globalization;
using System.Xml;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Formats;

/// <summary>The single pass over a drawing, and the state it needs to carry between elements.</summary>
internal sealed partial class DrawingGeometry
{
    private AnchorOrigin _horizontalFrom = AnchorOrigin.Column;
    private AnchorOrigin _verticalFrom = AnchorOrigin.Paragraph;
    private AnchorAlignment _horizontalAlignment;
    private AnchorAlignment _verticalAlignment;
    private TextWrapping _wrapping = TextWrapping.Square;
    private WrapSides _sides;
    private WordColor? _outlineColor;
    private long _offsetX;
    private long _offsetY;
    private long _outlineWidth;
    private long _distanceTop;
    private long _distanceBottom;
    private long _distanceLeft = PictureAnchor.DefaultSideDistance;
    private long _distanceRight = PictureAnchor.DefaultSideDistance;
    private List<PolygonPoint>? _polygon;
    private bool _behindText;
    private bool _fromDrawingMl;
    private int _pictures;
    private int _shapes;
    private int _filledShapes;

    /// <summary>Which value a piece of element text is about to supply.</summary>
    private enum Pending
    {
        None,
        Align,
        Offset,
    }

    /// <summary>Which of the two colours an <c>a:srgbClr</c> is about to supply.</summary>
    private enum Painting
    {
        None,
        Fill,
        Outline,
    }

    /// <summary>
    /// Walks the drawing one node at a time.
    /// </summary>
    /// <remarks>
    /// A few of the values are the text of an element rather than an attribute, and reading that
    /// text outright would move the reader past siblings the walk still needs. So an element that
    /// is about to supply one says so, and the text node that follows is taken as its value.
    /// </remarks>
    private sealed class Scanner(DrawingGeometry found)
    {
        private Pending _pending;
        private Painting _painting;
        private bool _horizontal = true;
        private int _shapeProperties = -1;
        private int _position = -1;
        private int _line = -1;
        private int _colour = -1;
        private int _polygon = -1;

        public void Step(XmlReader xml)
        {
            if (xml.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                Take(xml.Value);
                return;
            }

            if (xml.NodeType != XmlNodeType.Element)
                return;

            _pending = Pending.None;
            Element(xml);
        }

        private void Take(string text)
        {
            switch (_pending)
            {
                case Pending.Align when _horizontal:
                    found._horizontalAlignment = ParseAlignment(text);
                    break;
                case Pending.Align:
                    found._verticalAlignment = ParseAlignment(text);
                    break;
                case Pending.Offset when _horizontal:
                    found._offsetX = ParseLong(text.Trim());
                    break;
                case Pending.Offset:
                    found._offsetY = ParseLong(text.Trim());
                    break;
                default:
                    break;
            }

            _pending = Pending.None;
        }

        private void Element(XmlReader xml)
        {
            switch (xml.LocalName)
            {
                case "inline" when xml.NamespaceURI == DocxSchema.NsWordDrawing:
                    found.IsInline = true;
                    found._fromDrawingMl = true;
                    break;

                case "anchor" when xml.NamespaceURI == DocxSchema.NsWordDrawing:
                    found.IsAnchored = true;
                    found._fromDrawingMl = true;
                    found._behindText = XmlHelp.AttrBool(xml, "behindDoc") == true;
                    Distance(xml, "distT", ref found._distanceTop);
                    Distance(xml, "distB", ref found._distanceBottom);
                    Distance(xml, "distL", ref found._distanceLeft);
                    Distance(xml, "distR", ref found._distanceRight);
                    break;

                case "positionH" or "positionV" when xml.NamespaceURI == DocxSchema.NsWordDrawing:
                    Position(xml);
                    break;

                case "align" when _position >= 0 && xml.Depth == _position + 1:
                    _pending = Pending.Align;
                    break;

                case "posOffset" when _position >= 0 && xml.Depth == _position + 1:
                    _pending = Pending.Offset;
                    break;

                case "wrapNone" when xml.NamespaceURI == DocxSchema.NsWordDrawing:
                    found._wrapping = TextWrapping.None;
                    break;

                case "wrapTopAndBottom" when xml.NamespaceURI == DocxSchema.NsWordDrawing:
                    found._wrapping = TextWrapping.TopAndBottom;
                    break;

                case "wrapSquare" or "wrapTight" or "wrapThrough" when xml.NamespaceURI == DocxSchema.NsWordDrawing:
                    found._wrapping = xml.LocalName switch
                    {
                        "wrapTight" => TextWrapping.Tight,
                        "wrapThrough" => TextWrapping.Through,
                        _ => TextWrapping.Square,
                    };

                    found._sides = ParseSides(XmlHelp.Attr(xml, "wrapText"));
                    break;

                case "wrapPolygon" when xml.NamespaceURI == DocxSchema.NsWordDrawing:
                    _polygon = xml.Depth;
                    found._polygon = [];
                    break;

                case "start" or "lineTo" when _polygon >= 0 && xml.Depth == _polygon + 1:
                    found._polygon!.Add(new PolygonPoint(
                        ParseLong(XmlHelp.Attr(xml, "x")),
                        ParseLong(XmlHelp.Attr(xml, "y"))));
                    break;

                case "extent":
                    found.Width = ParseLong(XmlHelp.Attr(xml, "cx"));
                    found.Height = ParseLong(XmlHelp.Attr(xml, "cy"));
                    break;

                case "docPr":
                    found.Name = XmlHelp.Attr(xml, "name");
                    found.Description = XmlHelp.Attr(xml, "descr");
                    break;

                case "pic" when xml.NamespaceURI == DocxSchema.NsPicture:
                    found._pictures++;
                    break;

                case "wsp" when xml.NamespaceURI == DocxSchema.NsWordShape:
                    found._shapes++;
                    break;

                case "txbx" when xml.NamespaceURI == DocxSchema.NsWordShape:
                    found.HasText = true;
                    break;

                case "bodyPr" when xml.NamespaceURI == DocxSchema.NsWordShape:
                    if (XmlHelp.Attr(xml, "lIns") is { } leftInset)
                        found.TextInsetLeft = Length.FromEmu(ParseLong(leftInset));
                    if (XmlHelp.Attr(xml, "rIns") is { } rightInset)
                        found.TextInsetRight = Length.FromEmu(ParseLong(rightInset));
                    if (XmlHelp.Attr(xml, "tIns") is { } topInset)
                        found.TextInsetTop = Length.FromEmu(ParseLong(topInset));
                    if (XmlHelp.Attr(xml, "bIns") is { } bottomInset)
                        found.TextInsetBottom = Length.FromEmu(ParseLong(bottomInset));
                    found.TextFlow = XmlHelp.Attr(xml, "vert") switch
                    {
                        "vert" or "eaVert" => Styles.TextDirection.TopToBottomRightToLeft,
                        "vert270" => Styles.TextDirection.BottomToTopLeftToRight,
                        "mongolianVert" => Styles.TextDirection.TopToBottomLeftToRight,
                        _ => found.TextFlow,
                    };

                    break;

                case "spPr" when xml.NamespaceURI == DocxSchema.NsWordShape:
                    _shapeProperties = xml.Depth;
                    break;

                case "prstGeom" when _shapeProperties >= 0 && xml.Depth == _shapeProperties + 1:
                    found.IsLine = XmlHelp.Attr(xml, "prst") is "line" or "straightConnector1";
                    break;

                case "blipFill" when _shapeProperties >= 0 && xml.Depth == _shapeProperties + 1:
                    found._filledShapes++;
                    break;

                case "blip":
                    found.RelationshipId ??= XmlHelp.RelAttr(xml, "embed");
                    break;

                case "chart" when xml.NamespaceURI is DocxSchema.NsChart or DocxSchema.NsChartStrict:
                    found.ChartRelationshipId ??= XmlHelp.RelAttr(xml);
                    break;

                case "ln" when _shapeProperties >= 0 && xml.Depth == _shapeProperties + 1:
                    _line = xml.Depth;
                    found._outlineWidth = ParseLong(XmlHelp.Attr(xml, "w"));
                    break;

                case "solidFill" when _line >= 0 && xml.Depth == _line + 1:
                    Colour(xml, Painting.Outline);
                    break;

                case "solidFill" when _shapeProperties >= 0 && xml.Depth == _shapeProperties + 1:
                    Colour(xml, Painting.Fill);
                    break;

                case "srgbClr" when _painting != Painting.None && xml.Depth == _colour + 1:
                    Paint(XmlHelp.Attr(xml, "val"));
                    break;

                default:
                    if (xml.NamespaceURI == DocxSchema.NsVml)
                        Vml(xml);
                    else if (xml.LocalName == "wrap" && xml.NamespaceURI.EndsWith("office:word", StringComparison.Ordinal))
                        VmlWrap(xml);

                    break;
            }
        }

        /// <summary>Reads one wrap distance, leaving the default in place when the anchor is silent.</summary>
        private static void Distance(XmlReader xml, string attribute, ref long value)
        {
            if (XmlHelp.Attr(xml, attribute) is { Length: > 0 } text)
                value = ParseLong(text);
        }

        private void Position(XmlReader xml)
        {
            _horizontal = xml.LocalName == "positionH";
            _position = xml.Depth;

            AnchorOrigin origin = ParseOrigin(XmlHelp.Attr(xml, "relativeFrom"), _horizontal);
            if (_horizontal)
                found._horizontalFrom = origin;
            else
                found._verticalFrom = origin;
        }

        private void Colour(XmlReader xml, Painting painting)
        {
            _painting = painting;
            _colour = xml.Depth;
        }

        private void Paint(string? value)
        {
            if (ParseColor(value) is { } color)
            {
                if (_painting == Painting.Outline)
                    found._outlineColor = color;
                else
                    found.Fill = color;
            }

            _painting = Painting.None;
        }

        /// <summary>
        /// Reads the legacy branch. It only speaks for values the modern one did not give, so a
        /// drawing written both ways is understood from the modern half and a document converted
        /// out of the binary format, which has only this half, is understood all the same.
        /// </summary>
        private void Vml(XmlReader xml)
        {
            if (found._fromDrawingMl || XmlHelp.Attr(xml, "style") is not { Length: > 0 } style)
                return;

            found.Fill ??= ParseColor(XmlHelp.Attr(xml, "fillcolor"));
            if (ParseColor(XmlHelp.Attr(xml, "strokecolor")) is { } stroke)
            {
                found._outlineColor = stroke;
                found._outlineWidth = VmlLength(XmlHelp.Attr(xml, "strokeweight"))?.Emu ?? 0;
            }

            VmlStyle(style);
        }

        private void VmlStyle(string style)
        {
            bool absolute = false;

            foreach (Range part in style.AsSpan().Split(';'))
            {
                ReadOnlySpan<char> declaration = style.AsSpan()[part];
                int colon = declaration.IndexOf(':');
                if (colon < 0)
                    continue;

                string name = declaration[..colon].Trim().ToString();
                string value = declaration[(colon + 1)..].Trim().ToString();

                switch (name)
                {
                    case "position":
                        absolute = value == "absolute";
                        break;
                    case "width":
                        found.Width = VmlLength(value)?.Emu ?? found.Width;
                        break;
                    case "height":
                        found.Height = VmlLength(value)?.Emu ?? found.Height;
                        break;
                    case "margin-left" or "left":
                        found._offsetX = VmlLength(value)?.Emu ?? found._offsetX;
                        break;
                    case "margin-top" or "top":
                        found._offsetY = VmlLength(value)?.Emu ?? found._offsetY;
                        break;
                    case "mso-position-horizontal":
                        found._horizontalAlignment = ParseAlignment(value);
                        break;
                    case "mso-position-vertical":
                        found._verticalAlignment = ParseAlignment(value);
                        break;
                    case "mso-position-horizontal-relative":
                        found._horizontalFrom = ParseOrigin(value, horizontal: true);
                        break;
                    case "mso-position-vertical-relative":
                        found._verticalFrom = ParseOrigin(value, horizontal: false);
                        break;
                    case "mso-wrap-distance-left":
                        found._distanceLeft = VmlLength(value)?.Emu ?? found._distanceLeft;
                        break;
                    case "mso-wrap-distance-right":
                        found._distanceRight = VmlLength(value)?.Emu ?? found._distanceRight;
                        break;
                    case "mso-wrap-distance-top":
                        found._distanceTop = VmlLength(value)?.Emu ?? found._distanceTop;
                        break;
                    case "mso-wrap-distance-bottom":
                        found._distanceBottom = VmlLength(value)?.Emu ?? found._distanceBottom;
                        break;
                    case "layout-flow" when value is "vertical" or "vertical-ideographic":
                        found.TextFlow = Styles.TextDirection.TopToBottomRightToLeft;
                        break;
                    case "mso-layout-flow-alt" when value == "bottom-to-top":
                        found.TextFlow = Styles.TextDirection.BottomToTopLeftToRight;
                        break;
                    case "z-index":
                        found._behindText = value.StartsWith('-');
                        break;
                    default:
                        break;
                }
            }

            if (absolute)
                found.IsAnchored = true;
            else
                found.IsInline = true;
        }

        private void VmlWrap(XmlReader xml)
        {
            if (found._fromDrawingMl)
                return;

            found._wrapping = XmlHelp.Attr(xml, "type") switch
            {
                "none" => TextWrapping.None,
                "topAndBottom" => TextWrapping.TopAndBottom,
                "tight" => TextWrapping.Tight,
                "through" => TextWrapping.Through,
                _ => TextWrapping.Square,
            };

            found._sides = XmlHelp.Attr(xml, "side") switch
            {
                "left" => WrapSides.Left,
                "right" => WrapSides.Right,
                "largest" => WrapSides.Largest,
                _ => WrapSides.Both,
            };
        }

        /// <summary>
        /// A length as the legacy drawing layer writes it: a number and a unit, where the unit is
        /// pixels when it is left out.
        /// </summary>
        private static Length? VmlLength(string? value)
        {
            if (value is not { Length: > 0 })
                return null;

            ReadOnlySpan<char> text = value.AsSpan().Trim();
            int digits = text.Length;
            while (digits > 0 && !char.IsAsciiDigit(text[digits - 1]) && text[digits - 1] != '.')
                digits--;

            if (!double.TryParse(text[..digits], NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                return null;

            return text[digits..].Trim().ToString() switch
            {
                "in" => Length.FromInches(number),
                "cm" => Length.FromCentimeters(number),
                "mm" => Length.FromMillimeters(number),
                "pt" => Length.FromPoints(number),
                "pc" => Length.FromPoints(number * 12),
                _ => Length.FromPixels(number),
            };
        }
    }
}
