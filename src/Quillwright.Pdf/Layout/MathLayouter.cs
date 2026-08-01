using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Lays an equation out in two dimensions: what a fraction, a script or a radical actually looks
/// like, measured against the same font metrics as the text around it.
/// </summary>
/// <remarks>
/// <para>
/// This is typesetting on a budget. A mathematical font carries stretchy variants of every
/// bracket and radical and a table of how far a superscript should be raised for each of them;
/// none of that can be assumed of the fonts a document names, so a bracket that has to grow is
/// drawn at a larger size instead, and the offsets are the proportions Word uses. The result
/// reads correctly, which is the point — an equation drawn a little plainly is a great deal
/// better than the placeholder that used to be left in its place.
/// </para>
/// <para>
/// Every part is laid out against its own origin and then placed, so a nested fraction knows
/// nothing about where it ends up.
/// </para>
/// </remarks>
internal sealed partial class MathLayouter
{
    /// <summary>How much smaller a script, a limit or a degree is drawn.</summary>
    private const double ScriptScale = 0.65;

    /// <summary>How much smaller again a script inside a script is drawn.</summary>
    private const double NestedScale = 0.85;

    /// <summary>The smallest size anything in an equation is drawn at.</summary>
    private const double MinimumSize = 4;

    /// <summary>The size a run gets when nothing in the chain states one.</summary>
    private const double DefaultSize = 11;

    /// <summary>How thick a fraction bar is, as a fraction of the size it is drawn at.</summary>
    private const double RuleWeight = 0.055;

    /// <summary>How much room is left round a fraction bar, as a fraction of the size.</summary>
    private const double BarGap = 0.18;

    /// <summary>The gap either side of an operator or between a function and its argument.</summary>
    private const double ThinSpace = 0.16;

    private readonly TextMeasurer _measurer;

    internal MathLayouter(TextMeasurer measurer) => _measurer = measurer;

    /// <summary>
    /// Lays out an equation. A display paragraph holding several is stacked, each on a line of
    /// its own, which is what the paragraph means.
    /// </summary>
    /// <param name="equation">The equation to lay out.</param>
    /// <param name="format">The formatting of the run the equation is anchored in.</param>
    public EquationLayout Layout(MathObject equation, RunFormat format)
    {
        ArgumentNullException.ThrowIfNull(equation);
        ArgumentNullException.ThrowIfNull(format);

        if (equation.Equations.Count <= 1)
            return Element(equation.Content, format);

        return Stack([.. equation.Equations.Select(one => Element(one, format))], Size(format), centred: false);
    }

    /// <summary>Lays out one argument: its nodes side by side on a shared baseline.</summary>
    private EquationLayout Element(MathElement element, RunFormat format)
    {
        var layout = new EquationLayout();
        foreach (MathNode node in element.Nodes)
        {
            EquationLayout part = Node(node, format);
            layout.Place(part, layout.Width, 0);
        }

        // An empty argument still occupies a line's worth of height, so a fraction with an empty
        // numerator does not collapse onto its bar.
        if (element.Nodes.Count == 0)
            Empty(layout, format);

        return layout;
    }

    private EquationLayout Node(MathNode node, RunFormat format) => node switch
    {
        MathRun run => Run(run, format),
        RawMath raw => Text(raw.GetText(), format, italic: false),
        MathFraction fraction => Fraction(fraction, format),
        MathScript script => Script(script, format),
        MathRadical radical => Radical(radical, format),
        MathNary nary => Nary(nary, format),
        MathDelimiter delimiter => Delimiter(delimiter, format),
        MathFunction function => Function(function, format),
        MathMatrix matrix => Matrix(matrix, format),
        MathArray array => Stack([.. array.Rows.Select(row => Element(row, format))], Size(format), centred: true),
        MathLimit limit => Limit(limit, format),
        MathBar bar => Bar(bar, format),
        MathAccent accent => Accent(accent, format),
        MathGroupCharacter group => GroupCharacter(group, format),
        MathBorderBox box => BorderBox(box, format),
        MathPhantom phantom => Phantom(phantom, format),
        MathBox box => Element(box.Base, format),
        _ => new EquationLayout(),
    };

    /// <summary>
    /// Lays out a run. Letters in an equation are italic and everything else is upright, unless
    /// the run's own properties say otherwise, so a run is cut into stretches of one or the
    /// other and each is measured in the face it is drawn in.
    /// </summary>
    private EquationLayout Run(MathRun run, RunFormat format)
    {
        var layout = new EquationLayout();
        MathTextStyle style = MathTextStyle.Of(run.PropertiesXml);

        foreach ((string text, bool italic) in style.Split(run.Text))
            layout.Place(Text(text, style.Apply(format), italic), layout.Width, 0);

        if (run.Text.Length == 0)
            Empty(layout, format);

        return layout;
    }

    /// <summary>One stretch of characters, measured and placed on the baseline.</summary>
    private EquationLayout Text(string text, RunFormat format, bool italic)
    {
        var layout = new EquationLayout();
        if (text.Length == 0)
        {
            Empty(layout, format);
            return layout;
        }

        CharacterStyle style = _measurer.Style(format with { Italic = italic });
        layout.Marks.Add(new EquationMark(text, style, 0, 0));
        layout.Width = style.Measure(text);
        layout.Ascent = style.Ascent;
        layout.Descent = style.Descent;
        return layout;
    }

    /// <summary>Gives an empty part the height of a line, so that nothing collapses onto nothing.</summary>
    private void Empty(EquationLayout layout, RunFormat format)
    {
        CharacterStyle style = _measurer.Style(format);
        layout.Ascent = Math.Max(layout.Ascent, style.Ascent * 0.6);
        layout.Descent = Math.Max(layout.Descent, style.Descent * 0.6);
    }

    /// <summary>
    /// Stacks parts one above another on a common centre line, which is what an array of
    /// equations and a display paragraph of several both are.
    /// </summary>
    /// <param name="rows">The parts, top to bottom.</param>
    /// <param name="size">The size the gap between them is measured against.</param>
    /// <param name="centred">Whether to centre each row, rather than aligning them on the left.</param>
    private static EquationLayout Stack(IReadOnlyList<EquationLayout> rows, double size, bool centred)
    {
        var layout = new EquationLayout();
        if (rows.Count == 0)
            return layout;

        double gap = size * 0.35;
        double width = rows.Max(static row => row.Width);
        double total = rows.Sum(static row => row.Height) + (gap * (rows.Count - 1));

        // The stack sits astride the line it is on, with its middle a little above the baseline.
        double top = -(total / 2) - (size * 0.25);
        foreach (EquationLayout row in rows)
        {
            layout.Place(row, centred ? (width - row.Width) / 2 : 0, top + row.Ascent);
            top += row.Height + gap;
        }

        layout.Width = width;
        return layout;
    }

    /// <summary>The size a part is drawn at, in points.</summary>
    private static double Size(RunFormat format) => format.Size?.Points is > 0 and var points ? points : DefaultSize;

    /// <summary>The same formatting at a smaller size, never smaller than is worth drawing.</summary>
    private static RunFormat Smaller(RunFormat format, double scale) =>
        format with { Size = Length.FromPoints(Math.Max(MinimumSize, Size(format) * scale)) };

    /// <summary>
    /// The size a script inside this one is drawn at. Word reduces the first level a long way
    /// and every level after it only a little, which is why a doubly nested exponent stays
    /// readable.
    /// </summary>
    private static RunFormat ScriptSize(RunFormat format) =>
        Smaller(format, Size(format) < DefaultSize * ScriptScale ? NestedScale : ScriptScale);
}
