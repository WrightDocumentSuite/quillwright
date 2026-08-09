using Inkwright;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Text boxes: the fill, the frame and the words inside, drawn from the geometry the model read
/// off the shape's own markup.
/// </summary>
/// <remarks>
/// A box is a small page that never turns: content that does not fit is cut off at its bottom
/// edge, which is what Word does with a box that is not allowed to grow. The words are measured
/// once per shape and cached — a paragraph re-measured because of a float replays the same shape,
/// and its content must not be counted twice.
/// </remarks>
internal sealed partial class PageComposer
{
    private readonly Dictionary<Shape, List<BlockBox>> _shapeContent = [];

    /// <summary>Whether the words of a shape run down a turned box rather than across it.</summary>
    private static bool IsTurned(Shape shape) =>
        shape.Direction is TextDirection.TopToBottomRightToLeft or TextDirection.BottomToTopLeftToRight;

    /// <summary>
    /// The measured content of a shape — its paragraphs and its tables. Measured against the
    /// box's own width — or its height, when the words run down a turned box — which nothing
    /// outside the shape can change; that is what makes the cache safe wherever the box lands.
    /// </summary>
    private List<BlockBox> ShapeContent(Shape shape)
    {
        if (_shapeContent.TryGetValue(shape, out List<BlockBox>? cached))
            return cached;

        double inner = IsTurned(shape)
            ? Math.Max(1, shape.Height.Points - shape.InsetTop.Points - shape.InsetBottom.Points)
            : Math.Max(1, shape.Width.Points - shape.InsetLeft.Points - shape.InsetRight.Points);

        List<BlockBox> content = MeasureBlocks(Expand(shape.Content.Blocks), inner);
        _shapeContent[shape] = content;
        return content;
    }

    /// <summary>Draws a text box: background, frame, and as many of its lines as it can hold.</summary>
    /// <param name="shape">The shape to draw.</param>
    /// <param name="x">Its left edge, in points from the left of the page.</param>
    /// <param name="y">Its top edge, in points down from the top of the page.</param>
    /// <param name="furniture">Whether it belongs under the text rather than over it.</param>
    private void DrawShape(Shape shape, double x, double y, bool furniture)
    {
        double width = shape.Width.Points;
        double height = shape.Height.Points;
        List<PageItem> target = furniture ? Current.Furniture : Current.Items;

        if (shape.IsLine)
        {
            if (shape.Outline is { IsEmpty: false } line)
            {
                target.Add(new StrokeItem
                {
                    X = x,
                    Y = y,
                    X2 = x + width,
                    Y2 = y + height,
                    Thickness = Math.Max(0.25, line.Width.Points),
                    Color = _context.ColorOf(line.Color, PdfColor.Black),
                    Style = line.Style,
                });
            }

            return;
        }

        if (shape.Fill is { } fill && !fill.IsAuto)
        {
            target.Add(new FillItem
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Color = _context.ColorOf(fill, PdfColor.White),
            });
        }

        Frame(shape, x, y, width, height, target);
        DrawShapeContent(shape, x, y, height, target, furniture);
    }

    private void Frame(Shape shape, double x, double y, double width, double height, List<PageItem> target)
    {
        if (shape.Outline is not { } outline || outline.IsEmpty)
            return;

        double thickness = Math.Max(0.25, outline.Width.Points);
        PdfColor color = _context.ColorOf(outline.Color, PdfColor.Black);

        Span<(double X, double Y, double X2, double Y2)> edges =
        [
            (x, y, x + width, y),
            (x, y + height, x + width, y + height),
            (x, y, x, y + height),
            (x + width, y, x + width, y + height),
        ];

        foreach ((double ax, double ay, double bx, double by) in edges)
        {
            target.Add(new StrokeItem
            {
                X = ax,
                Y = ay,
                X2 = bx,
                Y2 = by,
                Thickness = thickness,
                Color = color,
                Style = outline.Style,
            });
        }
    }

    private void DrawShapeContent(
        Shape shape, double x, double y, double height, List<PageItem> target, bool furniture)
    {
        if (IsTurned(shape))
        {
            DrawTurnedShapeContent(shape, x, y, height, target, furniture);
            return;
        }

        double left = x + shape.InsetLeft.Points;
        double limit = y + height - shape.InsetBottom.Points;
        double cursor = y + shape.InsetTop.Points;

        foreach (BlockBox block in ShapeContent(shape))
        {
            cursor += block.SpacingBefore;

            bool fits = block switch
            {
                TableBox table => DrawShapeTable(table, left, ref cursor, limit, target, furniture),
                ParagraphBox paragraph => DrawShapeParagraph(paragraph, left, ref cursor, limit, target, furniture),
                _ => true,
            };

            if (!fits)
            {
                _context.Diagnostics.Add(
                    PdfExportWarningKind.LayoutApproximated,
                    "The content of a text box is taller than the box and was cut off at its bottom edge.",
                    "shape-overflow");
                return;
            }

            cursor += block.SpacingAfter;
        }
    }

    /// <summary>
    /// Draws the words of a turned box: the lines run along its height and stack across its
    /// width — towards the left when the text reads downwards, towards the right when it reads
    /// upwards. Lines that stack past the box's width are cut off and said.
    /// </summary>
    private void DrawTurnedShapeContent(
        Shape shape, double x, double y, double height, List<PageItem> target, bool furniture)
    {
        bool downwards = shape.Direction == TextDirection.TopToBottomRightToLeft;
        double width = shape.Width.Points;
        double length = Math.Max(1, height - shape.InsetTop.Points - shape.InsetBottom.Points);
        double room = width - shape.InsetLeft.Points - shape.InsetRight.Points;
        double offset = 0;

        foreach (BlockBox block in ShapeContent(shape))
        {
            if (block is not ParagraphBox paragraph)
                continue;

            offset += paragraph.SpacingBefore;

            foreach (LineBox line in paragraph.Lines)
            {
                if (offset + line.Height > room + 0.01)
                {
                    _context.Diagnostics.Add(
                        PdfExportWarningKind.LayoutApproximated,
                        "The lines of a turned text box stack wider than the box and were cut off.",
                        "vertical-overflow");
                    return;
                }

                double stripLeft = downwards
                    ? x + width - shape.InsetRight.Points - offset - line.Height
                    : x + shape.InsetLeft.Points + offset;

                target.Add(new TextLineItem
                {
                    Line = line,
                    X = stripLeft,
                    Y = y + shape.InsetTop.Points,
                    Length = length,
                    Rotation = downwards ? 90 : 270,
                    Tag = furniture ? FurnitureTag(paragraph) : TagOf(paragraph),
                });

                offset += line.Height;
            }

            offset += paragraph.SpacingAfter;
        }
    }

    /// <summary>Draws a paragraph inside a box, stopping at the line that would cross its bottom.</summary>
    private bool DrawShapeParagraph(
        ParagraphBox paragraph, double left, ref double cursor, double limit, List<PageItem> target, bool furniture)
    {
        foreach (LineBox line in paragraph.Lines)
        {
            if (cursor + line.Height > limit + 0.01)
                return false;

            double lineLeft = left + line.IndentLeft;
            TagRef? tag = furniture ? FurnitureTag(paragraph) : TagOf(paragraph);
            target.Add(new TextLineItem
            {
                Line = line,
                X = lineLeft,
                Y = cursor,
                Tag = tag,
                PaintComments = !furniture,
            });

            if (!furniture)
                AddLinks(line, lineLeft, cursor, tag);

            // A box inside a box: the inner one is drawn where its line landed, like any other.
            DrawInlineShapes(line, lineLeft, cursor, furniture);
            cursor += line.Height;
        }

        return true;
    }

    /// <summary>
    /// Draws a table inside a box, row by row, stopping at the row that would cross its bottom.
    /// The row machinery draws through the cursor and onto the page's items, so both are borrowed
    /// and put back — and what landed in the items is carried over to the furniture when the box
    /// itself is furniture.
    /// </summary>
    private bool DrawShapeTable(
        TableBox table, double left, ref double cursor, double limit, List<PageItem> target, bool furniture)
    {
        double x = left + table.Offset;
        double saved = _cursor;
        int before = Current.Items.Count;
        bool fits = true;

        for (int index = 0; index < table.Rows.Count; index++)
        {
            RowBox row = table.Rows[index];
            if (cursor + row.Height > limit + 0.01)
            {
                fits = false;
                break;
            }

            _cursor = cursor;
            DrawRow(table, index, x, row.Height);
            cursor += row.Height;
        }

        _cursor = saved;

        if (furniture && !ReferenceEquals(target, Current.Items))
        {
            for (int i = before; i < Current.Items.Count; i++)
                target.Add(Current.Items[i]);

            Current.Items.RemoveRange(before, Current.Items.Count - before);
        }

        return fits;
    }

    /// <summary>Draws the text boxes standing in a line, now that the line's place is known.</summary>
    /// <param name="line">The line just placed.</param>
    /// <param name="x">Where its text area starts, in points from the left of the page.</param>
    /// <param name="y">The top of the line box, in points down from the top of the page.</param>
    /// <param name="furniture">Whether the line went under the text rather than over it.</param>
    private void DrawInlineShapes(LineBox line, double x, double y, bool furniture = false)
    {
        foreach (InlineFragment fragment in line.Fragments)
        {
            if (fragment is not ShapeFragment shape)
                continue;

            // The box stands on the baseline, so its top is its whole height above it.
            double top = y + line.BaselineFromTop - shape.Ascent;
            DrawShape(shape.Shape, x + fragment.X, top, furniture);
        }
    }

    /// <summary>Places a paragraph's floating text boxes and makes them obstacles to the text.</summary>
    /// <param name="box">The paragraph whose shapes these are.</param>
    /// <param name="left">The left edge of the column the paragraph sits in.</param>
    /// <param name="top">Where the paragraph starts, in points down from the top of the page.</param>
    /// <param name="wrap">Whether the text after this point has to flow round them.</param>
    private void PlaceFloatingShapes(ParagraphBox box, double left, double top, bool wrap = true)
    {
        foreach (Shape shape in box.FloatingShapes)
        {
            double width = shape.Width.Points;
            double height = shape.Height.Points;
            if (width <= 0 || height <= 0)
                continue;

            PictureAnchor anchor = shape.Anchor ?? new PictureAnchor();
            double x = Horizontal(anchor, left, width);
            double y = Vertical(anchor, top, height);

            DrawShape(shape, x, y, furniture: anchor.BehindText);

            if (wrap && ObstacleFor(anchor, width, height, left, top) is { } obstacle)
                _wrapObstacles.Add(obstacle);
        }
    }
}
