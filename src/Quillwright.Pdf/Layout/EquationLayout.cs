namespace Quillwright.Pdf.Layout;

/// <summary>
/// A run of glyphs inside a laid-out equation, placed against the equation's own origin.
/// </summary>
/// <param name="Text">The characters.</param>
/// <param name="Style">How to draw them, at the size this part of the equation calls for.</param>
/// <param name="X">Distance from the equation's left edge.</param>
/// <param name="Y">
/// How far below the equation's baseline this run's own baseline sits; negative is above.
/// </param>
internal readonly record struct EquationMark(string Text, CharacterStyle Style, double X, double Y);

/// <summary>
/// A line drawn inside an equation: the bar of a fraction, the roof of a radical, an edge of a
/// framed box, a strike through one.
/// </summary>
/// <param name="X">Where the line starts, from the equation's left edge.</param>
/// <param name="Y">Where it starts, below the equation's baseline.</param>
/// <param name="X2">Where it ends.</param>
/// <param name="Y2">Where it ends.</param>
/// <param name="Thickness">How thick to stroke it.</param>
internal readonly record struct EquationRule(double X, double Y, double X2, double Y2, double Thickness);

/// <summary>
/// An equation reduced to what it draws: glyph runs and lines, with the room the whole thing
/// takes on the line it sits in.
/// </summary>
/// <remarks>
/// Laying an equation out and drawing it are separate jobs for the same reason the rest of the
/// renderer keeps them apart: the layout has to be measured before the composer knows which page
/// it lands on, and by then nothing about it may change.
/// </remarks>
internal sealed class EquationLayout
{
    /// <summary>How wide the equation is, in points.</summary>
    public double Width { get; set; }

    /// <summary>How far it reaches above its baseline.</summary>
    public double Ascent { get; set; }

    /// <summary>How far it reaches below its baseline.</summary>
    public double Descent { get; set; }

    /// <summary>The glyph runs, in the order they were laid out.</summary>
    public List<EquationMark> Marks { get; } = [];

    /// <summary>The lines.</summary>
    public List<EquationRule> Rules { get; } = [];

    /// <summary>How tall the whole thing is.</summary>
    public double Height => Ascent + Descent;

    /// <summary>
    /// Copies another layout into this one, moved by an offset, and grows this one's own extent
    /// to cover it.
    /// </summary>
    /// <param name="child">The layout to place.</param>
    /// <param name="dx">How far to the right to move it.</param>
    /// <param name="dy">How far down to move its baseline.</param>
    public void Place(EquationLayout child, double dx, double dy)
    {
        ArgumentNullException.ThrowIfNull(child);

        foreach (EquationMark mark in child.Marks)
            Marks.Add(mark with { X = mark.X + dx, Y = mark.Y + dy });

        foreach (EquationRule rule in child.Rules)
            Rules.Add(rule with { X = rule.X + dx, Y = rule.Y + dy, X2 = rule.X2 + dx, Y2 = rule.Y2 + dy });

        Cover(child, dx, dy);
    }

    /// <summary>Grows the extent to cover a child placed at an offset, without copying it.</summary>
    /// <param name="child">The layout that was placed.</param>
    /// <param name="dx">How far to the right it went.</param>
    /// <param name="dy">How far down its baseline went.</param>
    public void Cover(EquationLayout child, double dx, double dy)
    {
        ArgumentNullException.ThrowIfNull(child);

        Width = Math.Max(Width, dx + child.Width);
        Ascent = Math.Max(Ascent, child.Ascent - dy);
        Descent = Math.Max(Descent, child.Descent + dy);
    }
}
