namespace Quillwright.Model;

/// <summary>
/// An equation anchored in a paragraph (<c>m:oMath</c>, ISO/IEC 29500-1 §22.1.2.77, or
/// <c>m:oMathPara</c>, §22.1.2.78).
/// </summary>
/// <remarks>
/// <para>
/// The equation carries both a tree and the markup it was read from. An equation nobody
/// touched is written back as the bytes it arrived as, so the parts of §22.1 this version does
/// not model — the spacing, the breaking, the justification, the fonts — survive a round trip
/// untouched. Editing the tree gives that up: call <see cref="Invalidate"/> and the markup is
/// regenerated from what the model holds.
/// </para>
/// <para>
/// An equation is written directly under the paragraph rather than inside a run, which is
/// where WordprocessingML puts it.
/// </para>
/// </remarks>
public sealed class MathObject : InlineObject
{
    private readonly List<MathElement> _equations = [new()];

    /// <summary>
    /// The equations, one per line. An inline equation has exactly one; a display paragraph can
    /// hold several, drawn one under the other and aligned with each other.
    /// </summary>
    public IList<MathElement> Equations => _equations;

    /// <summary>What the equation says, or the first of them when there is more than one.</summary>
    public MathElement Content
    {
        get
        {
            if (_equations.Count == 0)
                _equations.Add(new MathElement());

            return _equations[0];
        }
    }

    /// <summary>
    /// Whether the equation is a display equation, drawn on a line of its own
    /// (<c>m:oMathPara</c>) rather than in the run of text.
    /// </summary>
    public bool IsDisplay { get; set; }

    /// <summary>
    /// How a display equation sits across the line (<c>m:oMathParaPr</c>, §22.1.2.79). An
    /// inline equation has no say in the matter and leaves this at its default.
    /// </summary>
    public MathJustification Justification { get; set; }

    /// <summary>
    /// The markup the equation was read from, when it came from a file. It is written back
    /// unchanged until <see cref="Invalidate"/> says the tree has moved on from it.
    /// </summary>
    public string? OriginalXml { get; set; }

    /// <inheritdoc />
    public override bool IsRunChild => false;

    /// <summary>
    /// Says the tree has been edited, so that saving writes what the tree holds rather than
    /// the markup it was read from.
    /// </summary>
    /// <remarks>
    /// The tree is an ordinary object graph a caller edits in place, so there is nothing to
    /// notice a change; a caller that changes one says so.
    /// </remarks>
    public void Invalidate() => IsDirty = true;

    /// <summary>Whether the markup has to be regenerated rather than written back.</summary>
    internal bool IsDirty { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// A display paragraph holding several equations reads as all of them, separated by a
    /// space: they are separate lines on the page, but text extraction produces a line already.
    /// </remarks>
    public override string? GetText()
    {
        if (_equations.Count == 1)
            return _equations[0].IsEmpty ? null : _equations[0].GetText();

        string text = string.Join(' ', _equations.Where(static one => !one.IsEmpty).Select(static one => one.GetText()));
        return text.Length == 0 ? null : text;
    }
}
