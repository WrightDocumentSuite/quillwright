using System.Text;

namespace Quillwright.Model;

/// <summary>
/// How a display equation sits across the line it is drawn on (<c>m:jc</c>, §22.1.2.51).
/// </summary>
public enum MathJustification : byte
{
    /// <summary>Not stated, so the setting in the document's math properties applies.</summary>
    Default = 0,

    /// <summary>Against the left margin.</summary>
    Left,

    /// <summary>Against the right margin.</summary>
    Right,

    /// <summary>In the middle of the line.</summary>
    Center,

    /// <summary>
    /// Centred as a group, so that several equations line up with each other rather than each
    /// being centred on its own.
    /// </summary>
    CenterGroup,
}

/// <summary>Something drawn inside a frame (<c>m:borderBox</c>, §22.1.2.11).</summary>
/// <remarks>
/// The frame is four edges and four strikes, each of which can be there or not, which is what
/// makes the element worth modelling: a box with three edges hidden and one strike is how a
/// cancelled term is written, and it means something quite different from a plain box.
/// </remarks>
public sealed class MathBorderBox : MathNode
{
    /// <summary>What the frame is drawn round.</summary>
    public MathElement Base { get; } = new();

    /// <summary>Whether the top edge is left out (<c>m:hideTop</c>, §22.1.2.47).</summary>
    public bool HideTop { get; set; }

    /// <summary>Whether the bottom edge is left out (<c>m:hideBot</c>, §22.1.2.44).</summary>
    public bool HideBottom { get; set; }

    /// <summary>Whether the left edge is left out (<c>m:hideLeft</c>, §22.1.2.45).</summary>
    public bool HideLeft { get; set; }

    /// <summary>Whether the right edge is left out (<c>m:hideRight</c>, §22.1.2.46).</summary>
    public bool HideRight { get; set; }

    /// <summary>Whether a line is drawn across the middle (<c>m:strikeH</c>, §22.1.2.108).</summary>
    public bool StrikeHorizontal { get; set; }

    /// <summary>Whether a line is drawn down the middle (<c>m:strikeV</c>, §22.1.2.110).</summary>
    public bool StrikeVertical { get; set; }

    /// <summary>
    /// Whether a line is drawn from the bottom left to the top right (<c>m:strikeBLTR</c>,
    /// §22.1.2.107).
    /// </summary>
    public bool StrikeUpward { get; set; }

    /// <summary>
    /// Whether a line is drawn from the top left to the bottom right (<c>m:strikeTLBR</c>,
    /// §22.1.2.109).
    /// </summary>
    public bool StrikeDownward { get; set; }

    /// <inheritdoc />
    public override string GetText() => Base.GetText();
}

/// <summary>
/// A stack of equations aligned with one another (<c>m:eqArr</c>, §22.1.2.34), which is how a
/// system of simultaneous equations is written.
/// </summary>
public sealed class MathArray : MathNode
{
    /// <summary>The equations, top to bottom.</summary>
    public IList<MathElement> Rows { get; } = [];

    /// <inheritdoc />
    /// <remarks>
    /// Read as a line, the rows are separated the way the rows of a matrix are, because on one
    /// line that is what they are.
    /// </remarks>
    public override string GetText()
    {
        var builder = new StringBuilder();
        foreach (MathElement row in Rows)
        {
            if (builder.Length > 0)
                builder.Append("; ");
            builder.Append(row.GetText());
        }

        return builder.ToString();
    }
}

/// <summary>
/// A base with something written under or over it (<c>m:limLow</c>, §22.1.2.54, and
/// <c>m:limUpp</c>, §22.1.2.56) — the range under a limit, the condition over a maximum.
/// </summary>
/// <remarks>
/// This is not a script: a script sits beside the base at the corner, while a limit sits
/// squarely under or over it, and the two elements are separate in the vocabulary for that
/// reason. The two of them differ only by which side the limit goes, so one node covers both.
/// </remarks>
public sealed class MathLimit : MathNode
{
    /// <summary>What the limit belongs to.</summary>
    public MathElement Base { get; } = new();

    /// <summary>What is written under or over it.</summary>
    public MathElement Limit { get; } = new();

    /// <summary>Which side of the base the limit sits on.</summary>
    public MathEdge Position { get; set; }

    /// <inheritdoc />
    public override string GetText() =>
        Base.GetText() + (Position == MathEdge.Top ? "^" : "_") + MathText.Group(Limit.GetText());
}

/// <summary>
/// Something that takes up room without being drawn (<c>m:phant</c>, §22.1.2.81), used to line
/// one equation up with another.
/// </summary>
public sealed class MathPhantom : MathNode
{
    /// <summary>What the space is measured from.</summary>
    public MathElement Base { get; } = new();

    /// <summary>
    /// Whether the contents are drawn as well as measured (<c>m:show</c>, §22.1.2.96). Absent
    /// means they are not, which is what makes the object a phantom.
    /// </summary>
    public bool Show { get; set; }

    /// <summary>Whether the space taken is no wider than nothing (<c>m:zeroWid</c>, §22.1.2.124).</summary>
    public bool ZeroWidth { get; set; }

    /// <summary>Whether the space taken reaches no higher than the baseline (<c>m:zeroAsc</c>, §22.1.2.122).</summary>
    public bool ZeroAscent { get; set; }

    /// <summary>Whether the space taken reaches no lower than the baseline (<c>m:zeroDesc</c>, §22.1.2.123).</summary>
    public bool ZeroDescent { get; set; }

    /// <summary>
    /// Whether the contents are drawn in the background colour rather than left out
    /// (<c>m:transp</c>, §22.1.2.117), which shows through anything behind them.
    /// </summary>
    public bool Transparent { get; set; }

    /// <inheritdoc />
    /// <remarks>A phantom nothing shows reads as nothing, because that is what it says.</remarks>
    public override string GetText() => Show ? Base.GetText() : string.Empty;
}
