using Inkwright;

namespace Quillwright.Pdf.Layout;

/// <summary>A filled shape inside a chart: a bar, a wedge of a pie, a swatch in the legend.</summary>
/// <param name="Fill">What to paint it in.</param>
/// <param name="Points">Its corners, clockwise, in the chart's own coordinates.</param>
internal readonly record struct ChartShape(PdfColor Fill, IReadOnlyList<(double X, double Y)> Points);

/// <summary>A line inside a chart: an axis, a gridline, or a series drawn as a line.</summary>
/// <param name="Color">What to stroke it in.</param>
/// <param name="Thickness">How thick.</param>
/// <param name="Points">The points it runs through, in the chart's own coordinates.</param>
internal readonly record struct ChartStroke(PdfColor Color, double Thickness, IReadOnlyList<(double X, double Y)> Points);

/// <summary>
/// A chart reduced to what it draws, in coordinates measured from the top-left of the frame the
/// document reserved for it.
/// </summary>
/// <remarks>
/// The layout is a plain list of shapes, lines and labels for the same reason the equation
/// layout is: composition decides what goes where and rendering only replays it, so a page can
/// be measured, thrown away and laid out again without redoing any of this.
/// </remarks>
internal sealed class ChartLayout
{
    /// <summary>How wide the frame is, in points.</summary>
    public double Width { get; set; }

    /// <summary>How tall it is, in points.</summary>
    public double Height { get; set; }

    /// <summary>The filled shapes, in the order they are painted.</summary>
    public List<ChartShape> Shapes { get; } = [];

    /// <summary>The lines.</summary>
    public List<ChartStroke> Strokes { get; } = [];

    /// <summary>
    /// The labels. Each mark's <c>Y</c> is the distance from the top of the frame down to the
    /// text's own baseline, rather than an offset from anything.
    /// </summary>
    public List<EquationMark> Labels { get; } = [];

    /// <summary>Whether the chart has anything at all to draw.</summary>
    public bool IsEmpty => Shapes.Count == 0 && Strokes.Count == 0 && Labels.Count == 0;
}
