using System.Globalization;
using Inkwright;
using Quillwright.Model;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// The three shapes a chart's numbers can take: bars standing on a category axis, lines running
/// across one, and wedges of a circle.
/// </summary>
internal sealed partial class ChartLayouter
{
    /// <summary>How many gridlines to rule across the value axis, at most.</summary>
    private const int GridLines = 4;

    private void Bars(ChartLayout layout, Chart chart, CharacterStyle style, in Area plot)
    {
        Scale scale = Scale.Of(chart);
        int categories = Categories(chart);
        if (categories == 0)
            return;

        Area inner = Axes(layout, chart, style, plot, scale, categories);
        double slot = inner.Width / categories;
        double barGroup = slot * 0.72;
        double bar = barGroup / chart.Series.Count;

        for (int c = 0; c < categories; c++)
        {
            double left = inner.Left + (slot * c) + ((slot - barGroup) / 2);
            for (int s = 0; s < chart.Series.Count; s++)
            {
                if (Value(chart.Series[s], c) is not { } value)
                    continue;

                double zero = scale.Y(inner, 0);
                double top = scale.Y(inner, value);
                layout.Shapes.Add(new ChartShape(
                    Palette[s % Palette.Length],
                    Box(left + (bar * s), Math.Min(zero, top), bar * 0.92, Math.Abs(zero - top))));
            }
        }
    }

    private void Lines(ChartLayout layout, Chart chart, CharacterStyle style, in Area plot)
    {
        Scale scale = Scale.Of(chart);
        int categories = Categories(chart);
        if (categories == 0)
            return;

        Area inner = Axes(layout, chart, style, plot, scale, categories);
        double step = categories > 1 ? inner.Width / (categories - 1) : 0;

        for (int s = 0; s < chart.Series.Count; s++)
        {
            var points = new List<(double X, double Y)>(categories);
            for (int c = 0; c < categories; c++)
            {
                if (Value(chart.Series[s], c) is { } value)
                    points.Add((inner.Left + (step * c) + (categories > 1 ? 0 : inner.Width / 2), scale.Y(inner, value)));
            }

            if (points.Count > 1)
                layout.Strokes.Add(new ChartStroke(Palette[s % Palette.Length], Math.Max(0.7, style.FontSize / 8), points));
        }
    }

    /// <summary>
    /// A pie of the first series, because a pie draws one series and a document that stored
    /// several drew the first of them.
    /// </summary>
    private void Pie(ChartLayout layout, Chart chart, in Area plot, bool doughnut)
    {
        double[] values = [.. chart.Series[0].Values.Select(static value => Math.Max(0, value ?? 0))];
        double total = values.Sum();
        if (total <= 0)
        {
            Report("A pie chart whose values add up to nothing is not drawn.", chart.Kind);
            return;
        }

        double radius = Math.Min(plot.Width, plot.Height) / 2;
        double cx = (plot.Left + plot.Right) / 2;
        double cy = (plot.Top + plot.Bottom) / 2;
        double hole = doughnut ? radius * 0.5 : 0;
        double from = -Math.PI / 2;

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] <= 0)
                continue;

            double sweep = values[i] / total * Math.PI * 2;
            layout.Shapes.Add(new ChartShape(Palette[i % Palette.Length], Wedge(cx, cy, radius, hole, from, sweep)));
            from += sweep;
        }
    }

    /// <summary>
    /// One slice, as a polygon: an arc of the outer circle and, for a doughnut, an arc of the
    /// inner one back again. Sixty-four sides to a full circle is smooth at any size a page
    /// prints a chart at.
    /// </summary>
    private static (double X, double Y)[] Wedge(double cx, double cy, double radius, double hole, double from, double sweep)
    {
        int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI * 2) * 64));
        var points = new List<(double X, double Y)>((steps + 1) * 2);

        for (int i = 0; i <= steps; i++)
            points.Add(At(cx, cy, radius, from + (sweep * i / steps)));

        if (hole <= 0)
        {
            points.Add((cx, cy));
            return [.. points];
        }

        for (int i = steps; i >= 0; i--)
            points.Add(At(cx, cy, hole, from + (sweep * i / steps)));

        return [.. points];

        static (double X, double Y) At(double cx, double cy, double r, double angle) =>
            (cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle)));
    }

    /// <summary>
    /// Rules the value axis and its gridlines, labels them, and names the categories along the
    /// bottom. Answers the rectangle the series themselves are drawn in, which is what is left
    /// once the labels have taken their room.
    /// </summary>
    private Area Axes(ChartLayout layout, Chart chart, CharacterStyle style, in Area plot, in Scale scale, int categories)
    {
        double labels = scale.Labels().Max(value => style.Measure(Format(value))) + (style.FontSize * 0.4);
        var inner = new Area(plot.Left + labels, plot.Top, plot.Right, plot.Bottom - style.LineHeight);
        if (inner.Height < 4 || inner.Width < 4)
            return plot;

        foreach (double value in scale.Labels())
        {
            double y = scale.Y(inner, value);
            layout.Strokes.Add(new ChartStroke(AxisColor, 0.4, [(inner.Left, y), (inner.Right, y)]));

            string text = Format(value);
            layout.Labels.Add(new EquationMark(text, style, inner.Left - style.Measure(text) - (style.FontSize * 0.3), y + (style.Ascent / 2)));
        }

        Categories(layout, chart, style, inner, categories);
        return inner;
    }

    /// <summary>Names as many categories along the bottom as will fit without overlapping.</summary>
    private static void Categories(ChartLayout layout, Chart chart, CharacterStyle style, in Area inner, int categories)
    {
        IReadOnlyList<string> names = chart.Series[0].Categories;
        double slot = inner.Width / categories;
        double baseline = inner.Bottom + style.Ascent + (style.FontSize * 0.2);
        double last = double.NegativeInfinity;

        for (int c = 0; c < categories && c < names.Count; c++)
        {
            if (names[c] is not { Length: > 0 } name)
                continue;

            double width = style.Measure(name);
            double x = inner.Left + (slot * c) + ((slot - width) / 2);
            if (x < last)
                continue;

            layout.Labels.Add(new EquationMark(name, style, Math.Max(inner.Left, x), baseline));
            last = x + width + (style.FontSize * 0.4);
        }
    }

    /// <summary>How many points the chart has along its category axis.</summary>
    private static int Categories(Chart chart) =>
        chart.Series.Max(static series => series.Values.Count);

    private static double? Value(ChartSeries series, int index) =>
        index < series.Values.Count ? series.Values[index] : null;

    /// <summary>An axis label, with no more decimals than the number needs.</summary>
    private static string Format(double value) =>
        Math.Abs(value) >= 1000 || value == Math.Round(value)
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// What the value axis runs between, rounded out to a step a person would choose, and how
    /// to turn a value into a distance down the plot area.
    /// </summary>
    /// <param name="Minimum">The bottom of the axis.</param>
    /// <param name="Maximum">The top of it.</param>
    /// <param name="Step">The gap between gridlines.</param>
    private readonly record struct Scale(double Minimum, double Maximum, double Step)
    {
        /// <summary>Works out an axis that covers every value a chart holds.</summary>
        public static Scale Of(Chart chart)
        {
            double[] values = [.. chart.Series.SelectMany(static series => series.Values).OfType<double>()];
            double high = values.Length > 0 ? values.Max() : 0;
            double low = values.Length > 0 ? values.Min() : 0;

            // A chart of positive numbers stands on zero; one that dips below it shows both.
            high = Math.Max(high, 0);
            low = Math.Min(low, 0);
            if (high - low < 1e-9)
                high = low + 1;

            double step = Nice((high - low) / GridLines);
            return new Scale(Math.Floor(low / step) * step, Math.Ceiling(high / step) * step, step);
        }

        /// <summary>Where a value sits, measured down from the top of the plot area.</summary>
        /// <param name="plot">The plot area.</param>
        /// <param name="value">The value.</param>
        public double Y(in Area plot, double value)
        {
            double span = Maximum - Minimum;
            double fraction = span <= 0 ? 0 : (value - Minimum) / span;
            return plot.Bottom - (fraction * plot.Height);
        }

        /// <summary>The values the axis is labelled at.</summary>
        public IEnumerable<double> Labels()
        {
            for (double value = Minimum; value <= Maximum + (Step / 2); value += Step)
                yield return value;
        }

        /// <summary>The nearest one, two or five times a power of ten, which is what an axis uses.</summary>
        private static double Nice(double rough)
        {
            if (rough <= 0)
                return 1;

            double power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double scaled = rough / power;
            return power * (scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10);
        }
    }
}
