using Quillwright.Model;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Pictures that do not flow with the text, and the room the text has to leave them.
/// </summary>
/// <remarks>
/// A floating picture is placed against something — a page edge, a margin, the paragraph it is
/// anchored in — and unless it says otherwise, the text keeps out of its rectangle: beside it in
/// the room it leaves, or past its bottom when it leaves none. Tight and through wrapping follow
/// the picture's outline in Word; here they keep clear of its rectangle instead, which is what
/// square wrapping does, and the difference is said in the diagnostics.
/// </remarks>
internal sealed partial class PageComposer
{
    /// <summary>What the text on the current page has to keep out of, in page coordinates.</summary>
    private readonly List<WrapObstacle> _wrapObstacles = [];

    /// <summary>Draws a paragraph's floats and, on the body flow, makes them obstacles to the text.</summary>
    /// <param name="box">The paragraph whose floats these are.</param>
    /// <param name="left">The left edge of the column the paragraph sits in.</param>
    /// <param name="top">Where the paragraph starts, in points down from the top of the page.</param>
    /// <param name="wrap">
    /// Whether the text after this point has to flow round them. Floats anchored inside table
    /// cells are drawn but not flowed round, which is said in the documentation.
    /// </param>
    private void PlaceFloats(ParagraphBox box, double left, double top, bool wrap = true)
    {
        foreach (Picture picture in box.Floats)
        {
            double width = picture.Width.Points;
            double height = picture.Height.Points;
            if (width <= 0 || height <= 0)
                continue;

            PictureAnchor anchor = picture.Anchor ?? new PictureAnchor();
            var item = new ImageItem
            {
                Picture = picture,
                Width = width,
                Height = height,
                X = Horizontal(anchor, left, width),
                Y = Vertical(anchor, top, height),
                Tag = FigureTag(picture, null),
            };

            // A picture behind the text is drawn with the furniture, which goes down first.
            if (anchor.BehindText)
                Current.Furniture.Add(item);
            else
                Current.Items.Add(item);

            if (wrap && ObstacleFor(anchor, width, height, left, top) is { } obstacle)
                _wrapObstacles.Add(obstacle);
        }
    }

    /// <summary>
    /// What a float keeps the text out of: its rectangle or its own wrapping polygon, grown by
    /// the clearances the anchor asks for. A float whose wrapping is none returns nothing,
    /// because the text ignores it.
    /// </summary>
    private WrapObstacle? ObstacleFor(PictureAnchor anchor, double width, double height, double left, double top)
    {
        if (width <= 0 || height <= 0 || anchor.Wrapping == TextWrapping.None)
            return null;

        double x = Horizontal(anchor, left, width);
        double y = Vertical(anchor, top, height);

        // Each kind of wrapping honours its own clearances: square all four, an outline only
        // the sides, top-and-bottom only the caps — which is which attributes its element takes.
        bool outlined = anchor.Wrapping is TextWrapping.Tight or TextWrapping.Through;
        double clearLeft = anchor.Wrapping == TextWrapping.TopAndBottom ? 0 : anchor.DistanceLeft.Points;
        double clearRight = anchor.Wrapping == TextWrapping.TopAndBottom ? 0 : anchor.DistanceRight.Points;
        double clearTop = outlined ? 0 : anchor.DistanceTop.Points;
        double clearBottom = outlined ? 0 : anchor.DistanceBottom.Points;

        if (outlined && Outline(anchor, x, y, width, height) is { } outline)
        {
            if (anchor.Wrapping == TextWrapping.Through)
            {
                _context.Diagnostics.Add(
                    PdfExportWarningKind.LayoutApproximated,
                    "Text wraps round a through-wrapped object without entering its interior, the way tight wrapping does.",
                    "wrap-through");
            }

            double minX = outline.Min(static point => point.X);
            double maxX = outline.Max(static point => point.X);
            double minY = outline.Min(static point => point.Y);
            double maxY = outline.Max(static point => point.Y);

            return new WrapObstacle(
                minX - clearLeft, maxX + clearRight, minY - clearTop, maxY + clearBottom,
                anchor.Sides, Blocking: false)
            {
                Outline = outline,
                ClearLeft = clearLeft,
                ClearRight = clearRight,
            };
        }

        if (outlined)
        {
            _context.Diagnostics.Add(
                PdfExportWarningKind.LayoutApproximated,
                "Text wraps round the rectangle of a floating object whose markup states no wrapping polygon.",
                "wrap-tight");
        }

        return new WrapObstacle(
            x - clearLeft,
            x + width + clearRight,
            y - clearTop,
            y + height + clearBottom,
            anchor.Sides,
            anchor.Wrapping == TextWrapping.TopAndBottom);
    }

    /// <summary>
    /// The anchor's wrapping polygon in page coordinates. The points count in 21600ths of the
    /// object's size — the fixed space Word writes them in — so each is scaled by the size the
    /// object actually has and moved to where it actually sits.
    /// </summary>
    private static (double X, double Y)[]? Outline(PictureAnchor anchor, double x, double y, double width, double height)
    {
        if (anchor.Polygon.Count < 3)
            return null;

        var points = new (double X, double Y)[anchor.Polygon.Count];
        for (int i = 0; i < anchor.Polygon.Count; i++)
        {
            points[i] = (
                x + (anchor.Polygon[i].X / 21600.0 * width),
                y + (anchor.Polygon[i].Y / 21600.0 * height));
        }

        return points;
    }

    /// <summary>
    /// The shape a paragraph starting at <paramref name="top"/> flows into: the obstacles already
    /// on the page, plus the paragraph's own floats where they will land once it is placed here.
    /// Nothing overlapping means no shape, and the paragraph keeps its plain measurement.
    /// </summary>
    private FlowShape? ShapeFor(ParagraphBox box, double top)
    {
        if (_wrapObstacles.Count == 0 && box.Floats.Count == 0 && box.FloatingShapes.Count == 0)
            return null;

        List<WrapObstacle> all = [.. _wrapObstacles];
        foreach (Picture picture in box.Floats)
        {
            PictureAnchor anchor = picture.Anchor ?? new PictureAnchor();
            if (ObstacleFor(anchor, picture.Width.Points, picture.Height.Points, CurrentLeft, top) is { } obstacle)
                all.Add(obstacle);
        }

        foreach (Shape shape in box.FloatingShapes)
        {
            PictureAnchor anchor = shape.Anchor ?? new PictureAnchor();
            if (ObstacleFor(anchor, shape.Width.Points, shape.Height.Points, CurrentLeft, top) is { } obstacle)
                all.Add(obstacle);
        }

        return FlowShape.For(all, CurrentLeft, CurrentWidth, top);
    }

    private double Horizontal(PictureAnchor anchor, double left, double width)
    {
        PageGeometry geometry = Current.Geometry;
        (double origin, double extent) = anchor.HorizontalFrom switch
        {
            AnchorOrigin.Page => (0d, geometry.Width),
            AnchorOrigin.Character => (left, geometry.ContentRight - left),
            AnchorOrigin.Column => (left, CurrentWidth),
            _ => (geometry.ContentLeft, geometry.ContentWidth),
        };

        return Align(anchor.HorizontalAlignment, origin, extent, width, anchor.OffsetX.Points);
    }

    private double Vertical(PictureAnchor anchor, double top, double height)
    {
        PageGeometry geometry = Current.Geometry;
        (double origin, double extent) = anchor.VerticalFrom switch
        {
            AnchorOrigin.Page => (0d, geometry.Height),
            AnchorOrigin.Margin => (geometry.ContentTop, geometry.ContentHeight),
            _ => (top, geometry.ContentBottom - top),
        };

        return Align(anchor.VerticalAlignment, origin, extent, height, anchor.OffsetY.Points);
    }

    /// <summary>Where an edge lands: at the offset it names, or lined up with the origin it names.</summary>
    private static double Align(AnchorAlignment alignment, double origin, double extent, double size, double offset) =>
        alignment switch
        {
            AnchorAlignment.Center => origin + ((extent - size) / 2),
            AnchorAlignment.End or AnchorAlignment.Outside => origin + extent - size,
            AnchorAlignment.Start or AnchorAlignment.Inside => origin,
            _ => origin + offset,
        };
}
