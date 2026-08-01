using System.Globalization;
using Inkwright;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Draws a chart from the numbers the document cached for it.
/// </summary>
/// <remarks>
/// <para>
/// A chart part says a great deal about how it is drawn — fills, gradients, three-dimensional
/// rotations, a hundred kinds of label — and this reads none of it. What it reads is what the
/// chart is *of*: the kind, the title, the series and the categories and values behind them,
/// which is what the reader already models. From that it draws the chart plainly, in the
/// colours Office uses, which is a great deal closer to the document than the empty rectangle
/// that used to be left in its place.
/// </para>
/// <para>
/// The kinds that can be drawn from cached values alone are bars, lines and pies. Anything else
/// keeps its space and says in the diagnostics that it was not drawn, because a scatter plot
/// drawn as a bar chart would be worse than nothing.
/// </para>
/// </remarks>
internal sealed partial class ChartLayouter
{
    /// <summary>The accent colours Office gives the first series of a chart.</summary>
    private static readonly PdfColor[] Palette =
    [
        PdfColor.FromRgb(0x4472C4),
        PdfColor.FromRgb(0xED7D31),
        PdfColor.FromRgb(0xA5A5A5),
        PdfColor.FromRgb(0xFFC000),
        PdfColor.FromRgb(0x5B9BD5),
        PdfColor.FromRgb(0x70AD47),
    ];

    private static readonly PdfColor AxisColor = PdfColor.FromRgb(0xBFBFBF);
    private static readonly PdfColor LabelColor = PdfColor.FromRgb(0x595959);

    /// <summary>How much of the frame is left as a margin round everything.</summary>
    private const double Margin = 0.06;

    private readonly TextMeasurer _measurer;
    private readonly PdfExportDiagnostics _diagnostics;

    internal ChartLayouter(TextMeasurer measurer, PdfExportDiagnostics diagnostics)
    {
        _measurer = measurer;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Draws a chart into the frame the document reserved, or reports why it could not be.
    /// </summary>
    /// <param name="frame">Where the chart sits and how big it is.</param>
    /// <param name="chart">The chart part, or <see langword="null"/> when it could not be found.</param>
    /// <param name="format">The formatting of the run the frame is anchored in.</param>
    public ChartLayout? Layout(ChartFrame frame, Chart? chart, RunFormat format)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(format);

        double width = frame.Width.Points;
        double height = frame.Height.Points;
        if (width <= 1 || height <= 1)
            return null;

        var layout = new ChartLayout { Width = width, Height = height };
        if (chart is null || chart.Series.Count == 0)
        {
            Report("A chart with no cached values is not drawn.", chart?.Kind);
            return layout;
        }

        CharacterStyle style = LabelStyle(format, height);
        double top = Title(layout, chart, style, width);
        double bottom = height * (1 - Margin);
        double legend = Legend(layout, chart, style, width, bottom);

        var plot = new Area(width * Margin, top, width * (1 - Margin), legend);
        if (plot.Height < 8 || plot.Width < 16)
            return layout;

        Plot(layout, chart, style, plot);
        return layout;
    }

    /// <summary>Draws the series into the plot area, in whichever way the chart's kind calls for.</summary>
    private void Plot(ChartLayout layout, Chart chart, CharacterStyle style, in Area plot)
    {
        switch (chart.Kind)
        {
            case ChartKind.Bar:
                Bars(layout, chart, style, plot);
                return;

            case ChartKind.Line or ChartKind.Area or ChartKind.Scatter:
                Lines(layout, chart, style, plot);
                return;

            case ChartKind.Pie or ChartKind.Doughnut:
                Pie(layout, chart, plot, chart.Kind == ChartKind.Doughnut);
                return;

            default:
                Report("A chart of this kind is not drawn; its space is left blank.", chart.Kind);
                return;
        }
    }

    /// <summary>Puts the chart's title across the top and answers where the rest may start.</summary>
    private double Title(ChartLayout layout, Chart chart, CharacterStyle style, double width)
    {
        double top = layout.Height * Margin;
        if (chart.Title is not { Length: > 0 } title)
            return top;

        double text = style.Measure(title);
        layout.Labels.Add(new EquationMark(title, style, Math.Max(0, (width - text) / 2), top + style.Ascent));
        return top + style.LineHeight;
    }

    /// <summary>
    /// Puts a swatch and a name for each series along the bottom, and answers where the plot
    /// area has to stop. A chart of one series names it in its title or not at all.
    /// </summary>
    private double Legend(ChartLayout layout, Chart chart, CharacterStyle style, double width, double bottom)
    {
        List<string> names = [.. chart.Series.Select(static (series, i) => series.Name ?? Ordinal(i))];
        if (chart.Series.Count < 2 || chart.Kind is ChartKind.Pie or ChartKind.Doughnut)
            return bottom;

        double swatch = style.FontSize * 0.7;
        double gap = style.FontSize * 0.5;
        double total = names.Sum(name => swatch + (gap / 2) + style.Measure(name)) + (gap * (names.Count - 1));
        double x = Math.Max(0, (width - total) / 2);
        double baseline = bottom - style.Descent;
        double top = baseline - style.Ascent + ((style.Ascent - swatch) / 2);

        for (int i = 0; i < names.Count; i++)
        {
            layout.Shapes.Add(new ChartShape(Palette[i % Palette.Length], Box(x, top, swatch, swatch)));
            x += swatch + (gap / 2);
            layout.Labels.Add(new EquationMark(names[i], style, x, baseline));
            x += style.Measure(names[i]) + gap;
        }

        return bottom - style.LineHeight;
    }

    private void Report(string message, ChartKind? kind) =>
        _diagnostics.Add(PdfExportWarningKind.ContentSkipped, message, kind?.ToString() ?? "chart");

    /// <summary>The face the labels of a chart are drawn in: small, grey, never italic.</summary>
    private CharacterStyle LabelStyle(RunFormat format, double height)
    {
        // Labels scale with the frame rather than with the text around it, so a chart the size
        // of a stamp does not get eight-point axis numbers written across it.
        double size = Math.Clamp(height / 22, 4.5, 9);
        return _measurer.Style(format with
        {
            Size = Length.FromPoints(size),
            Bold = false,
            Italic = false,
            Color = WordColor.FromRgb(0x595959),
        });
    }

    /// <summary>The four corners of an upright rectangle.</summary>
    private static (double X, double Y)[] Box(double x, double y, double width, double height) =>
        [(x, y), (x + width, y), (x + width, y + height), (x, y + height)];

    /// <summary>The name a series with none is given, which is what Word shows in its place.</summary>
    private static string Ordinal(int index) =>
        "Series " + (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>A rectangle of the frame, in the chart's own coordinates.</summary>
    /// <param name="Left">Its left edge.</param>
    /// <param name="Top">Its top edge.</param>
    /// <param name="Right">Its right edge.</param>
    /// <param name="Bottom">Its bottom edge.</param>
    private readonly record struct Area(double Left, double Top, double Right, double Bottom)
    {
        /// <summary>How wide it is.</summary>
        public double Width => Right - Left;

        /// <summary>How tall it is.</summary>
        public double Height => Bottom - Top;
    }
}
