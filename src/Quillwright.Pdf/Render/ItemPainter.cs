using System.Globalization;
using Inkwright;
using Inkwright.Content;
using Inkwright.Cos;
using Inkwright.Images;
using Quillwright.Pdf.Layout;
using Quillwright.Styles;

namespace Quillwright.Pdf.Render;

/// <summary>Draws one page's items onto its canvas.</summary>
internal sealed partial class ItemPainter
{
    /// <summary>How far below the baseline an underline sits, as a fraction of the size.</summary>
    private const double UnderlineOffset = 0.12;

    /// <summary>How thick an underline is, as a fraction of the size.</summary>
    private const double UnderlineWeight = 0.06;

    /// <summary>How far above the baseline a strikethrough sits, as a fraction of the size.</summary>
    private const double StrikeOffset = 0.26;

    private readonly ContentCanvas _canvas;
    private readonly PdfPage _page;
    private readonly ComposedPage _composed;
    private readonly PageGeometry _geometry;
    private readonly ImageEmbedder _images;
    private readonly Func<PageFieldFragment, string> _resolveField;
    private readonly Action<Inkwright.Annotations.PdfLinkAnnotation, string> _destinations;
    private readonly ITagSink? _tags;

    internal ItemPainter(
        ContentCanvas canvas,
        PdfPage page,
        ComposedPage composed,
        ImageEmbedder images,
        Func<PageFieldFragment, string> resolveField,
        Action<Inkwright.Annotations.PdfLinkAnnotation, string> destinations,
        ITagSink? tags)
    {
        _canvas = canvas;
        _page = page;
        _composed = composed;
        _geometry = composed.Geometry;
        _images = images;
        _resolveField = resolveField;
        _destinations = destinations;
        _tags = tags;
    }

    /// <summary>Draws one item.</summary>
    /// <param name="item">The item to draw.</param>
    /// <remarks>
    /// In a tagged document every mark on the page must either belong to the structure tree or say
    /// that it belongs to nobody. Shading, rules and everything else the layout draws around the
    /// text is the latter, so it is wrapped in an artifact sequence rather than left unaccounted for.
    /// </remarks>
    public void Paint(PageItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        bool artifact = _tags is not null && item.Tag is null && item is not LinkItem;
        if (artifact)
            _canvas.BeginMarkedContent(PdfName.Get("Artifact"));

        Draw(item);

        if (artifact)
            _canvas.EndMarkedContent();
    }

    private void Draw(PageItem item)
    {
        switch (item)
        {
            case FillItem fill:
                PaintFill(fill);
                break;
            case StrokeItem stroke:
                PaintStroke(stroke);
                break;
            case TextLineItem text:
                PaintLine(text);
                break;
            case ImageItem image:
                PaintImage(image);
                break;
            case LinkItem link:
                PaintLink(link);
                break;
            default:
                break;
        }
    }

    private void PaintFill(FillItem fill)
    {
        if (fill.Width <= 0 || fill.Height <= 0)
            return;

        _canvas.Save()
               .FillColor(fill.Color)
               .Rectangle(fill.X, _geometry.ToPdfY(fill.Y + fill.Height), fill.Width, fill.Height)
               .Fill()
               .Restore();
    }

    private void PaintStroke(StrokeItem stroke)
    {
        if (stroke.Thickness <= 0)
            return;

        _canvas.Save().StrokeColor(stroke.Color).LineWidth(stroke.Thickness);

        if (DashOf(stroke.Style, stroke.Thickness) is { Length: > 0 } dash)
            _canvas.Dash(0, dash);

        double y1 = _geometry.ToPdfY(stroke.Y);
        double y2 = _geometry.ToPdfY(stroke.Y2);

        if (IsDoubled(stroke.Style))
        {
            // A double line is two thin lines, so the pair covers the thickness the border claims.
            double gap = stroke.Thickness;
            (double dx, double dy) = Normal(stroke.X, y1, stroke.X2, y2, gap);
            _canvas.LineWidth(Math.Max(0.25, stroke.Thickness / 3));
            Segment(stroke.X + dx, y1 + dy, stroke.X2 + dx, y2 + dy);
            Segment(stroke.X - dx, y1 - dy, stroke.X2 - dx, y2 - dy);
        }
        else
        {
            Segment(stroke.X, y1, stroke.X2, y2);
        }

        _canvas.Restore();
    }

    private void Segment(double x1, double y1, double x2, double y2) =>
        _canvas.MoveTo(x1, y1).LineTo(x2, y2).Stroke();

    /// <summary>The offset that separates the two halves of a doubled line.</summary>
    private static (double Dx, double Dy) Normal(double x1, double y1, double x2, double y2, double distance)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        return length < 1e-6 ? (0, 0) : (-dy / length * distance / 2, dx / length * distance / 2);
    }

    private static bool IsDoubled(BorderStyle style) =>
        style is BorderStyle.Double or BorderStyle.DoubleWave or BorderStyle.Triple;

    private static double[] DashOf(BorderStyle style, double thickness) => style switch
    {
        BorderStyle.Dotted => [thickness, thickness * 2],
        BorderStyle.Dashed or BorderStyle.DashSmallGap => [thickness * 3, thickness * 2],
        BorderStyle.DotDash => [thickness * 3, thickness * 2, thickness, thickness * 2],
        BorderStyle.DotDotDash => [thickness * 3, thickness * 2, thickness, thickness * 2, thickness, thickness * 2],
        _ => [],
    };

    private void PaintLine(TextLineItem item)
    {
        if (item.Rotation != 0)
        {
            PaintTurnedLine(item);
            return;
        }

        PaintFragments(item.Line, item.X, item.Y + item.Line.BaselineFromTop, item.Tag);
    }

    /// <summary>
    /// Draws a line turned on its side. The trick is that the turned frame imitates the page —
    /// same height, top-down coordinates — so every fragment painter works in it unchanged; one
    /// matrix carries the whole frame onto the strip the line occupies.
    /// </summary>
    private void PaintTurnedLine(TextLineItem item)
    {
        LineBox line = item.Line;
        double page = _geometry.Height;
        _canvas.Save();

        if (item.Rotation == 90)
        {
            // Read downwards, glyph tops to the right: the frame turns clockwise about the
            // strip's top-left corner, and the line's top edge lands on the strip's right.
            _canvas.Transform(PdfMatrix.Translation(item.X + line.Height - page, page - item.Y));
            _canvas.Transform(PdfMatrix.Rotation(-90));
        }
        else
        {
            // Read upwards, glyph tops to the left: counter-clockwise, starting at the strip's
            // bottom, which is its top-left moved down by its length.
            _canvas.Transform(PdfMatrix.Translation(item.X + page, page - item.Y - item.Length));
            _canvas.Transform(PdfMatrix.Rotation(90));
        }

        PaintFragments(line, 0, line.BaselineFromTop, item.Tag);
        _canvas.Restore();
    }

    private void PaintFragments(LineBox line, double originX, double baseline, TagRef? tag)
    {
        foreach (InlineFragment fragment in line.Fragments)
        {
            double x = originX + fragment.X;
            switch (fragment)
            {
                case TextFragment text:
                    PaintText(text, x, baseline, line.ExtraSpaceWidth, tag);
                    break;
                case PageFieldFragment field:
                    PaintFieldText(field, x, baseline, tag);
                    break;
                case TabFragment tab:
                    PaintTab(tab, x, baseline);
                    break;
                case ImageFragment image:
                    PaintInlineImage(image, x, baseline, image.Tag ?? tag);
                    break;
                case EquationFragment equation:
                    PaintEquation(equation, x, baseline, equation.Tag ?? tag);
                    break;
                case ChartFragment chart:
                    PaintChart(chart, x, baseline - chart.Ascent, chart.Tag ?? tag);
                    break;
                default:
                    break;
            }
        }
    }
    private void PaintTab(TabFragment tab, double x, double baseline)
    {
        if (tab.IsBar)
        {
            double top = baseline - tab.Style.Ascent;
            PaintStroke(new StrokeItem
            {
                X = x,
                Y = top,
                X2 = x,
                Y2 = baseline + tab.Style.Descent,
                Thickness = 0.5,
                Color = tab.Style.Color,
            });
            return;
        }

        if (tab.Leader == TabLeader.None || tab.Width <= 0.5)
            return;

        if (tab.Leader is TabLeader.Underscore or TabLeader.Heavy)
        {
            double thickness = tab.Leader == TabLeader.Heavy ? 1.5 : 0.6;
            Rule(x, baseline + (tab.Style.FontSize * UnderlineOffset), x + tab.Width, thickness,
                tab.Style.Color, BorderStyle.Single);
            return;
        }

        string glyph = tab.Leader switch
        {
            TabLeader.Hyphen => "-",
            TabLeader.MiddleDot => "\u00B7",
            _ => ".",
        };

        double glyphWidth = tab.Style.Measure(glyph);
        if (glyphWidth <= 0)
            return;

        int count = (int)Math.Floor(tab.Width / glyphWidth);
        if (count <= 0)
            return;

        // The dots hug the stop the tab runs to, which is what a table of contents looks like.
        double filled = count * glyphWidth;
        var run = new TextFragment
        {
            Text = string.Concat(Enumerable.Repeat(glyph, count)),
            Style = tab.Style,
            Width = filled,
        };

        PaintText(run, x + tab.Width - filled, baseline, extraSpace: 0, tag: null);
    }

    private void PaintInlineImage(ImageFragment fragment, double x, double baseline, TagRef? tag) =>
        PaintPicture(fragment.Picture, x, baseline - fragment.Ascent, fragment.Width, fragment.Ascent + fragment.Descent, tag);

    private void PaintImage(ImageItem item) =>
        PaintPicture(item.Picture, item.X, item.Y, item.Width, item.Height, item.Tag);

    private void PaintPicture(Model.Picture picture, double x, double top, double width, double height, TagRef? tag)
    {
        if (width <= 0 || height <= 0 || _images.Embed(picture.Image) is not { } image)
            return;

        int mcid = BeginTag(tag);
        _canvas.DrawImage(image, PdfRectangle.FromSize(x, _geometry.ToPdfY(top + height), width, height));
        EndTag(tag, mcid);
    }

    private void PaintLink(LinkItem link)
    {
        PdfRectangle bounds = PdfRectangle.FromSize(
            link.X, _geometry.ToPdfY(link.Y + link.Height), link.Width, link.Height);

        if (!string.IsNullOrEmpty(link.Url))
        {
            _page.Annotations.AddLink(bounds, link.Url);
            return;
        }

        if (string.IsNullOrEmpty(link.Anchor))
            return;

        // A link inside the document cannot be pointed anywhere until every page exists, so it is
        // created now and aimed once the render is over.
        _destinations(_page.Annotations.AddLink(bounds, "#"), link.Anchor);
    }

    private int BeginTag(TagRef? tag)
    {
        if (tag is null || _tags is null)
            return -1;

        int mcid = _tags.Next(tag);
        var properties = new PdfDictionary(1);
        properties.Set(PdfName.Get("MCID"), PdfValue.Integer(mcid));
        _canvas.BeginMarkedContent(PdfName.Get(tag.Tag), properties);
        return mcid;
    }

    private void EndTag(TagRef? tag, int mcid)
    {
        if (tag is null || _tags is null || mcid < 0)
            return;

        _canvas.EndMarkedContent();
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"Painter for page {_composed.Number.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Where the painter reports the marked-content sequences it writes, so a structure tree can point
/// at them. Only used when the export is tagged.
/// </summary>
internal interface ITagSink
{
    /// <summary>Reserves the next marked-content identifier and attaches it to a structure element.</summary>
    /// <param name="tag">The element the sequence belongs to.</param>
    int Next(TagRef tag);
}
