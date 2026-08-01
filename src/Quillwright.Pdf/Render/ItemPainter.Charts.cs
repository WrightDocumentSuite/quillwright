using Inkwright;
using Quillwright.Pdf.Layout;

namespace Quillwright.Pdf.Render;

/// <summary>
/// Drawing a chart: the shapes it fills, the lines it rules and the words it labels them with,
/// all in coordinates the layout measured from the top-left of the frame.
/// </summary>
internal sealed partial class ItemPainter
{
    /// <summary>Paints a chart into the frame the document reserved for it.</summary>
    /// <param name="fragment">The laid-out chart.</param>
    /// <param name="x">Where the frame's left edge lands on the page.</param>
    /// <param name="top">Where its top edge lands.</param>
    /// <param name="tag">The structure element it belongs to, when the export is tagged.</param>
    private void PaintChart(ChartFragment fragment, double x, double top, TagRef? tag)
    {
        ChartLayout chart = fragment.Layout;
        int mcid = BeginTag(tag);

        foreach (ChartShape shape in chart.Shapes)
            FillPolygon(shape, x, top);

        foreach (ChartStroke stroke in chart.Strokes)
            StrokePolyline(stroke, x, top);

        foreach (EquationMark label in chart.Labels)
        {
            var run = new TextFragment { Text = label.Text, Style = label.Style, Width = label.Style.Measure(label.Text) };
            PaintText(run, x + label.X, top + label.Y, extraSpace: 0, tag: null, marked: false);
        }

        EndTag(tag, mcid);
    }

    private void FillPolygon(in ChartShape shape, double x, double top)
    {
        if (shape.Points.Count < 3)
            return;

        _canvas.Save().FillColor(shape.Fill);
        _canvas.MoveTo(x + shape.Points[0].X, _geometry.ToPdfY(top + shape.Points[0].Y));

        for (int i = 1; i < shape.Points.Count; i++)
            _canvas.LineTo(x + shape.Points[i].X, _geometry.ToPdfY(top + shape.Points[i].Y));

        _canvas.ClosePath().Fill().Restore();
    }

    private void StrokePolyline(in ChartStroke stroke, double x, double top)
    {
        if (stroke.Points.Count < 2)
            return;

        _canvas.Save().StrokeColor(stroke.Color).LineWidth(Math.Max(0.25, stroke.Thickness));
        _canvas.MoveTo(x + stroke.Points[0].X, _geometry.ToPdfY(top + stroke.Points[0].Y));

        for (int i = 1; i < stroke.Points.Count; i++)
            _canvas.LineTo(x + stroke.Points[i].X, _geometry.ToPdfY(top + stroke.Points[i].Y));

        _canvas.Stroke().Restore();
    }
}
