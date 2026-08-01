using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// The objects that put something above, below or around their base: a matrix, a limit, a bar,
/// an accent, a stretched character, a framed box and a phantom.
/// </summary>
internal sealed partial class MathLayouter
{
    private EquationLayout Matrix(MathMatrix matrix, RunFormat format)
    {
        double size = Size(format);
        double columnGap = size * 0.6;
        double rowGap = size * 0.3;

        List<List<EquationLayout>> cells = [.. matrix.Rows.Select(row => row.Cells.Select(cell => Element(cell, format)).ToList())];
        if (cells.Count == 0)
            return new EquationLayout();

        int columns = cells.Max(static row => row.Count);
        double[] widths = new double[columns];
        foreach (List<EquationLayout> row in cells)
        {
            for (int i = 0; i < row.Count; i++)
                widths[i] = Math.Max(widths[i], row[i].Width);
        }

        double total = cells.Sum(static row => row.Count == 0 ? 0 : row.Max(static cell => cell.Height))
            + (rowGap * (cells.Count - 1));

        var layout = new EquationLayout();
        double top = -(total / 2) - (size * 0.25);

        foreach (List<EquationLayout> row in cells)
        {
            double height = row.Count == 0 ? 0 : row.Max(static cell => cell.Height);
            double ascent = row.Count == 0 ? 0 : row.Max(static cell => cell.Ascent);
            double x = 0;

            for (int i = 0; i < row.Count; i++)
            {
                layout.Place(row[i], x + ((widths[i] - row[i].Width) / 2), top + ascent);
                x += widths[i] + columnGap;
            }

            top += height + rowGap;
        }

        layout.Width = widths.Sum() + (columnGap * Math.Max(0, columns - 1));
        return layout;
    }

    /// <summary>
    /// A limit written squarely under or over its base, rather than at the corner where a script
    /// would sit.
    /// </summary>
    private EquationLayout Limit(MathLimit limit, RunFormat format)
    {
        double size = Size(format);
        EquationLayout basis = Element(limit.Base, format);
        EquationLayout value = Element(limit.Limit, ScriptSize(format));
        double gap = size * 0.08;

        var layout = new EquationLayout { Width = Math.Max(basis.Width, value.Width) };
        layout.Place(basis, (layout.Width - basis.Width) / 2, 0);
        layout.Place(
            value,
            (layout.Width - value.Width) / 2,
            limit.Position == MathEdge.Top
                ? -basis.Ascent - gap - value.Descent
                : basis.Descent + gap + value.Ascent);

        return layout;
    }

    private EquationLayout Bar(MathBar bar, RunFormat format)
    {
        double size = Size(format);
        EquationLayout basis = Element(bar.Base, format);
        double gap = size * 0.1;
        double thickness = size * RuleWeight;

        var layout = new EquationLayout();
        layout.Place(basis, 0, 0);

        double y = bar.Position == MathEdge.Top ? -basis.Ascent - gap : basis.Descent + gap;
        layout.Rules.Add(new EquationRule(0, y, basis.Width, y, thickness));

        if (bar.Position == MathEdge.Top)
            layout.Ascent = Math.Max(layout.Ascent, -y + thickness);
        else
            layout.Descent = Math.Max(layout.Descent, y + thickness);

        return layout;
    }

    /// <summary>A mark centred over the base, drawn at the size of the text around it.</summary>
    private EquationLayout Accent(MathAccent accent, RunFormat format)
    {
        EquationLayout basis = Element(accent.Base, format);
        var layout = new EquationLayout();
        layout.Place(basis, 0, 0);

        if (accent.Character.Length == 0)
            return layout;

        CharacterStyle style = _measurer.Style(format with { Italic = false });
        double width = style.Measure(accent.Character);
        double y = -basis.Ascent - (Size(format) * 0.04);

        layout.Marks.Add(new EquationMark(accent.Character, style, Math.Max(0, (basis.Width - width) / 2), y));
        layout.Ascent = Math.Max(layout.Ascent, -y + style.Ascent);
        return layout;
    }

    /// <summary>A character stretched across the base, above or below it.</summary>
    private EquationLayout GroupCharacter(MathGroupCharacter group, RunFormat format)
    {
        EquationLayout basis = Element(group.Base, format);
        var layout = new EquationLayout();
        layout.Place(basis, 0, 0);

        if (group.Character.Length == 0)
            return layout;

        CharacterStyle style = Widened(group.Character, format, basis.Width);
        double width = style.Measure(group.Character);
        double x = Math.Max(0, (basis.Width - width) / 2);
        double gap = Size(format) * 0.05;

        if (group.Position == MathEdge.Top)
        {
            double y = -basis.Ascent - gap - style.Descent;
            layout.Marks.Add(new EquationMark(group.Character, style, x, y));
            layout.Ascent = Math.Max(layout.Ascent, -y + style.Ascent);
        }
        else
        {
            double y = basis.Descent + gap + style.Ascent;
            layout.Marks.Add(new EquationMark(group.Character, style, x, y));
            layout.Descent = Math.Max(layout.Descent, y + style.Descent);
        }

        layout.Width = Math.Max(basis.Width, width);
        return layout;
    }

    /// <summary>The size a brace has to be drawn at to reach across what it groups.</summary>
    private CharacterStyle Widened(string glyph, RunFormat format, double width)
    {
        CharacterStyle plain = _measurer.Style(format with { Italic = false });
        double natural = plain.Measure(glyph);
        if (natural <= 0 || width <= natural)
            return plain;

        double scale = Math.Clamp(width / natural, 1, 4);
        return _measurer.Style(format with { Italic = false, Size = Primitives.Length.FromPoints(Size(format) * scale) });
    }

    /// <summary>A frame round the base, with whichever edges and strikes it asks for.</summary>
    private EquationLayout BorderBox(MathBorderBox box, RunFormat format)
    {
        double size = Size(format);
        double padding = size * 0.15;
        double thickness = size * RuleWeight;

        EquationLayout basis = Element(box.Base, format);
        var layout = new EquationLayout();
        layout.Place(basis, padding, 0);

        double left = 0;
        double right = basis.Width + (padding * 2);
        double top = -basis.Ascent - padding;
        double bottom = basis.Descent + padding;

        Edge(layout, !box.HideTop, left, top, right, top, thickness);
        Edge(layout, !box.HideBottom, left, bottom, right, bottom, thickness);
        Edge(layout, !box.HideLeft, left, top, left, bottom, thickness);
        Edge(layout, !box.HideRight, right, top, right, bottom, thickness);

        double middle = (top + bottom) / 2;
        Edge(layout, box.StrikeHorizontal, left, middle, right, middle, thickness);
        Edge(layout, box.StrikeVertical, (left + right) / 2, top, (left + right) / 2, bottom, thickness);
        Edge(layout, box.StrikeUpward, left, bottom, right, top, thickness);
        Edge(layout, box.StrikeDownward, left, top, right, bottom, thickness);

        layout.Width = right;
        layout.Ascent = Math.Max(layout.Ascent, -top + thickness);
        layout.Descent = Math.Max(layout.Descent, bottom + thickness);
        return layout;
    }

    private static void Edge(EquationLayout layout, bool drawn, double x, double y, double x2, double y2, double thickness)
    {
        if (drawn)
            layout.Rules.Add(new EquationRule(x, y, x2, y2, thickness));
    }

    /// <summary>
    /// A phantom takes the room its contents would and draws nothing, unless it says its
    /// contents are shown. The three zero settings take that room away again, one dimension at
    /// a time, which is what makes a phantom useful for lining two equations up.
    /// </summary>
    private EquationLayout Phantom(MathPhantom phantom, RunFormat format)
    {
        EquationLayout contents = Element(phantom.Base, format);
        var layout = new EquationLayout
        {
            Width = phantom.ZeroWidth ? 0 : contents.Width,
            Ascent = phantom.ZeroAscent ? 0 : contents.Ascent,
            Descent = phantom.ZeroDescent ? 0 : contents.Descent,
        };

        // Transparent means drawn in the colour of the paper, which on a page is not drawn.
        if (phantom.Show && !phantom.Transparent)
            layout.Marks.AddRange(contents.Marks);

        return layout;
    }
}
