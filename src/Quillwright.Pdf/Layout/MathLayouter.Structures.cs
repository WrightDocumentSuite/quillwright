using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// The objects of an equation that are two-dimensional: one thing above another, one thing
/// beside another, and the lines and brackets drawn round them.
/// </summary>
internal sealed partial class MathLayouter
{
    private EquationLayout Fraction(MathFraction fraction, RunFormat format)
    {
        if (fraction.Kind is MathFractionKind.Linear or MathFractionKind.Skewed)
            return Linear(fraction, format);

        double size = Size(format);
        double gap = size * BarGap;
        EquationLayout numerator = Element(fraction.Numerator, format);
        EquationLayout denominator = Element(fraction.Denominator, format);

        // The bar sits where a minus sign would, so a fraction lines up with the text beside it.
        double bar = -size * 0.28;
        var layout = new EquationLayout { Width = Math.Max(numerator.Width, denominator.Width) };
        double padding = size * 0.1;
        layout.Width += padding * 2;

        layout.Place(numerator, (layout.Width - numerator.Width) / 2, bar - gap - numerator.Descent);
        layout.Place(denominator, (layout.Width - denominator.Width) / 2, bar + gap + denominator.Ascent);

        if (fraction.Kind == MathFractionKind.Bar)
            layout.Rules.Add(new EquationRule(padding / 2, bar, layout.Width - (padding / 2), bar, size * RuleWeight));

        return layout;
    }

    /// <summary>A fraction written on one line, which is what the linear and skewed forms are.</summary>
    private EquationLayout Linear(MathFraction fraction, RunFormat format)
    {
        var layout = new EquationLayout();
        layout.Place(Element(fraction.Numerator, format), 0, 0);
        layout.Place(Text("/", format, italic: false), layout.Width, 0);
        layout.Place(Element(fraction.Denominator, format), layout.Width, 0);
        return layout;
    }

    private EquationLayout Script(MathScript script, RunFormat format)
    {
        double size = Size(format);
        RunFormat small = ScriptSize(format);
        EquationLayout basis = Element(script.Base, format);

        EquationLayout? sub = script.Subscript.IsEmpty ? null : Element(script.Subscript, small);
        EquationLayout? sup = script.Superscript.IsEmpty ? null : Element(script.Superscript, small);

        double rise = -Math.Max(size * 0.42, basis.Ascent - (size * 0.22));
        double drop = Math.Max(size * 0.18, basis.Descent + (size * 0.1));

        var layout = new EquationLayout();
        double scripts = Math.Max(sub?.Width ?? 0, sup?.Width ?? 0);

        // A pre-script hangs off the left of its base and everything else off the right.
        if (script.Placement == MathScriptPlacement.Before)
        {
            Attach(layout, sub, sup, 0, rise, drop);
            layout.Place(basis, scripts, 0);
        }
        else
        {
            layout.Place(basis, 0, 0);
            Attach(layout, sub, sup, basis.Width, rise, drop);
        }

        layout.Width = script.Placement == MathScriptPlacement.Before
            ? scripts + basis.Width
            : basis.Width + scripts;

        return layout;
    }

    /// <summary>Places a subscript and a superscript one above the other at the same left edge.</summary>
    private static void Attach(EquationLayout layout, EquationLayout? sub, EquationLayout? sup, double x, double rise, double drop)
    {
        if (sup is not null)
            layout.Place(sup, x, rise);
        if (sub is not null)
            layout.Place(sub, x, drop);
    }

    /// <summary>
    /// A radical: the sign itself, grown to the height of what it covers, and a roof drawn from
    /// the top of it across the radicand.
    /// </summary>
    private EquationLayout Radical(MathRadical radical, RunFormat format)
    {
        double size = Size(format);
        EquationLayout radicand = Element(radical.Base, format);
        double gap = size * 0.14;
        double height = radicand.Height + gap;

        CharacterStyle sign = Stretched("\u221A", format, height);
        double signWidth = sign.Measure("\u221A");

        var layout = new EquationLayout();
        double left = 0;

        if (!radical.HideDegree && !radical.Degree.IsEmpty)
        {
            EquationLayout degree = Element(radical.Degree, Smaller(format, ScriptScale * 0.9));
            layout.Place(degree, 0, -radicand.Ascent - (gap * 0.5));
            left = degree.Width;
        }

        layout.Marks.Add(new EquationMark("\u221A", sign, left, radicand.Descent));
        layout.Cover(new EquationLayout { Ascent = sign.Ascent - radicand.Descent }, left, 0);

        double body = left + signWidth;
        layout.Place(radicand, body, 0);

        double roof = -radicand.Ascent - gap;
        layout.Rules.Add(new EquationRule(body - (size * 0.02), roof, body + radicand.Width, roof, size * RuleWeight));
        layout.Ascent = Math.Max(layout.Ascent, -roof + (size * RuleWeight));
        layout.Width = body + radicand.Width;
        return layout;
    }

    /// <summary>
    /// An operator applied over a range. A sum or a product carries its limits under and over
    /// it; an integral carries them at its corners, which is what Word does with each.
    /// </summary>
    private EquationLayout Nary(MathNary nary, RunFormat format)
    {
        double size = Size(format);
        RunFormat small = ScriptSize(format);
        EquationLayout body = Element(nary.Base, format);

        CharacterStyle sign = Stretched(nary.Operator, format, size * 1.5);
        var operatorLayout = new EquationLayout { Width = sign.Measure(nary.Operator), Ascent = sign.Ascent, Descent = sign.Descent };
        operatorLayout.Marks.Add(new EquationMark(nary.Operator, sign, 0, 0));

        EquationLayout? lower = nary.HideLower || nary.Lower.IsEmpty ? null : Element(nary.Lower, small);
        EquationLayout? upper = nary.HideUpper || nary.Upper.IsEmpty ? null : Element(nary.Upper, small);

        EquationLayout head = UnderOver(nary.Operator)
            ? Stacked(operatorLayout, lower, upper, size)
            : Cornered(operatorLayout, lower, upper, size);

        var layout = new EquationLayout();
        layout.Place(head, 0, 0);
        layout.Place(body, head.Width + (size * ThinSpace), 0);
        layout.Width = head.Width + (size * ThinSpace) + body.Width;
        return layout;
    }

    /// <summary>
    /// Whether an operator carries its limits above and below rather than at its corners. The
    /// integrals are the exception; every other large operator stacks.
    /// </summary>
    private static bool UnderOver(string glyph) =>
        glyph.Length > 0 && glyph[0] is not (>= '\u222B' and <= '\u2233');

    /// <summary>Puts the limits above and below the operator, each centred on it.</summary>
    private static EquationLayout Stacked(EquationLayout head, EquationLayout? lower, EquationLayout? upper, double size)
    {
        double width = Math.Max(head.Width, Math.Max(lower?.Width ?? 0, upper?.Width ?? 0));
        double gap = size * 0.1;
        var layout = new EquationLayout { Width = width };

        layout.Place(head, (width - head.Width) / 2, 0);
        if (upper is not null)
            layout.Place(upper, (width - upper.Width) / 2, -head.Ascent - gap - upper.Descent);
        if (lower is not null)
            layout.Place(lower, (width - lower.Width) / 2, head.Descent + gap + lower.Ascent);

        return layout;
    }

    /// <summary>Puts the limits at the corners of the operator, the way a script sits.</summary>
    private static EquationLayout Cornered(EquationLayout head, EquationLayout? lower, EquationLayout? upper, double size)
    {
        var layout = new EquationLayout();
        layout.Place(head, 0, 0);
        Attach(layout, lower, upper, head.Width, -head.Ascent + (size * 0.3), head.Descent);
        layout.Width = head.Width + Math.Max(lower?.Width ?? 0, upper?.Width ?? 0);
        return layout;
    }

    private EquationLayout Delimiter(MathDelimiter delimiter, RunFormat format)
    {
        double size = Size(format);
        var parts = new List<EquationLayout>(delimiter.Arguments.Count);
        foreach (MathElement argument in delimiter.Arguments)
            parts.Add(Element(argument, format));

        if (parts.Count == 0)
            parts.Add(Element(new MathElement(), format));

        double height = Math.Max(size, parts.Max(static part => part.Height));
        var layout = new EquationLayout();

        Bracket(layout, delimiter.Begin, format, height);
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0)
                Bracket(layout, delimiter.Separator, format, height);

            layout.Place(parts[i], layout.Width, 0);
        }

        Bracket(layout, delimiter.End, format, height);
        return layout;
    }

    /// <summary>Draws one bracket at whatever size it takes to cover the height asked for.</summary>
    private void Bracket(EquationLayout layout, string glyph, RunFormat format, double height)
    {
        if (glyph.Length == 0)
            return;

        CharacterStyle style = Stretched(glyph, format, height);
        double width = style.Measure(glyph);

        // A bracket sits astride the middle of what it holds rather than on the baseline.
        double shift = (style.Ascent - style.Descent - (height * 0.72)) / 2;
        layout.Marks.Add(new EquationMark(glyph, style, layout.Width, shift));
        layout.Cover(new EquationLayout { Width = width, Ascent = style.Ascent, Descent = style.Descent }, layout.Width, shift);
        layout.Width += width;
    }

    /// <summary>
    /// The same formatting at whatever size makes a glyph as tall as it needs to be, within
    /// reason: a bracket round half a page would be one glyph the height of a page.
    /// </summary>
    private CharacterStyle Stretched(string glyph, RunFormat format, double height)
    {
        CharacterStyle plain = _measurer.Style(format with { Italic = false });
        double natural = plain.Ascent + plain.Descent;
        if (natural <= 0 || glyph.Length == 0)
            return plain;

        double scale = Math.Clamp(height / (natural * 0.78), 1, 4);
        return scale <= 1.02
            ? plain
            : _measurer.Style(format with { Italic = false, Size = Primitives.Length.FromPoints(Size(format) * scale) });
    }

    private EquationLayout Function(MathFunction function, RunFormat format)
    {
        var layout = new EquationLayout();
        layout.Place(Element(function.Name, format), 0, 0);
        layout.Place(Element(function.Argument, format), layout.Width + (Size(format) * ThinSpace), 0);
        return layout;
    }
}
