using System.Text;

namespace Quillwright.Model;

/// <summary>How a fraction is drawn (<c>m:type</c>, §22.1.2.118).</summary>
public enum MathFractionKind : byte
{
    /// <summary>Numerator above denominator, separated by a rule.</summary>
    Bar = 0,

    /// <summary>Numerator and denominator either side of a slanted rule.</summary>
    Skewed,

    /// <summary>Numerator and denominator on one line, separated by a solidus.</summary>
    Linear,

    /// <summary>Numerator above denominator with no rule between them.</summary>
    NoBar,
}

/// <summary>Which side of its base a script sits on.</summary>
public enum MathScriptPlacement : byte
{
    /// <summary>After the base (<c>m:sSub</c>, <c>m:sSup</c>, <c>m:sSubSup</c>).</summary>
    After = 0,

    /// <summary>Before the base (<c>m:sPre</c>, §22.1.2.99).</summary>
    Before,
}

/// <summary>Which edge of its base a bar or grouping character sits on (<c>m:pos</c>, §22.1.2.84).</summary>
public enum MathEdge : byte
{
    /// <summary>Below the base.</summary>
    Bottom = 0,

    /// <summary>Above the base.</summary>
    Top,
}

/// <summary>A fraction (<c>m:f</c>, §22.1.2.36).</summary>
public sealed class MathFraction : MathNode
{
    /// <summary>What is divided.</summary>
    public MathElement Numerator { get; } = new();

    /// <summary>What it is divided by.</summary>
    public MathElement Denominator { get; } = new();

    /// <summary>How the fraction is drawn.</summary>
    public MathFractionKind Kind { get; set; }

    /// <inheritdoc />
    public override string GetText() =>
        MathText.Group(Numerator.GetText()) + "/" + MathText.Group(Denominator.GetText());
}

/// <summary>A radical (<c>m:rad</c>, §22.1.2.88).</summary>
public sealed class MathRadical : MathNode
{
    /// <summary>What the root is taken of.</summary>
    public MathElement Base { get; } = new();

    /// <summary>Which root it is, when the radical shows one.</summary>
    public MathElement Degree { get; } = new();

    /// <summary>Whether the degree is drawn (<c>m:degHide</c>, §22.1.2.27).</summary>
    public bool HideDegree { get; set; }

    /// <inheritdoc />
    public override string GetText() =>
        (HideDegree || Degree.IsEmpty ? string.Empty : Degree.GetText()) + "√" + MathText.Group(Base.GetText());
}

/// <summary>
/// A base with a subscript, a superscript or both (<c>m:sSub</c>, <c>m:sSup</c>,
/// <c>m:sSubSup</c> and <c>m:sPre</c>, §22.1.2.99 to §22.1.2.106).
/// </summary>
/// <remarks>
/// The four elements differ only in which scripts they carry and which side of the base they
/// sit on, so one node covers all of them.
/// </remarks>
public sealed class MathScript : MathNode
{
    /// <summary>What is written large.</summary>
    public MathElement Base { get; } = new();

    /// <summary>What is written below, when there is one.</summary>
    public MathElement Subscript { get; } = new();

    /// <summary>What is written above, when there is one.</summary>
    public MathElement Superscript { get; } = new();

    /// <summary>Which side of the base the scripts sit on.</summary>
    public MathScriptPlacement Placement { get; set; }

    /// <inheritdoc />
    public override string GetText()
    {
        var scripts = new StringBuilder();
        if (!Subscript.IsEmpty)
            scripts.Append('_').Append(MathText.Group(Subscript.GetText()));
        if (!Superscript.IsEmpty)
            scripts.Append('^').Append(MathText.Group(Superscript.GetText()));

        return Placement == MathScriptPlacement.Before
            ? scripts.ToString() + Base.GetText()
            : Base.GetText() + scripts;
    }
}

/// <summary>A sum, product, integral or other operator over a range (<c>m:nary</c>, §22.1.2.70).</summary>
public sealed class MathNary : MathNode
{
    /// <summary>The operator drawn large, which defaults to a summation sign.</summary>
    public string Operator { get; set; } = "\u2211";

    /// <summary>Where the range starts.</summary>
    public MathElement Lower { get; } = new();

    /// <summary>Where it ends.</summary>
    public MathElement Upper { get; } = new();

    /// <summary>What the operator is applied to.</summary>
    public MathElement Base { get; } = new();

    /// <summary>Whether the lower limit is drawn (<c>m:subHide</c>, §22.1.2.113).</summary>
    public bool HideLower { get; set; }

    /// <summary>Whether the upper limit is drawn (<c>m:supHide</c>, §22.1.2.115).</summary>
    public bool HideUpper { get; set; }

    /// <inheritdoc />
    public override string GetText()
    {
        var builder = new StringBuilder(Operator);
        if (!HideLower && !Lower.IsEmpty)
            builder.Append('_').Append(MathText.Group(Lower.GetText()));
        if (!HideUpper && !Upper.IsEmpty)
            builder.Append('^').Append(MathText.Group(Upper.GetText()));

        return MathText.Join(builder.ToString(), Base.GetText());
    }
}

/// <summary>Something inside brackets (<c>m:d</c>, §22.1.2.24).</summary>
public sealed class MathDelimiter : MathNode
{
    /// <summary>The opening bracket (<c>m:begChr</c>).</summary>
    public string Begin { get; set; } = "(";

    /// <summary>The closing bracket (<c>m:endChr</c>).</summary>
    public string End { get; set; } = ")";

    /// <summary>What stands between the arguments when there is more than one (<c>m:sepChr</c>).</summary>
    public string Separator { get; set; } = "|";

    /// <summary>What the brackets hold, one argument per separator.</summary>
    public IList<MathElement> Arguments { get; } = [];

    /// <inheritdoc />
    public override string GetText()
    {
        var builder = new StringBuilder(Begin);
        for (int i = 0; i < Arguments.Count; i++)
        {
            if (i > 0)
                builder.Append(Separator);
            builder.Append(Arguments[i].GetText());
        }

        return builder.Append(End).ToString();
    }
}

/// <summary>A named function applied to something (<c>m:func</c>, §22.1.2.39).</summary>
public sealed class MathFunction : MathNode
{
    /// <summary>The function's name, such as <c>sin</c>.</summary>
    public MathElement Name { get; } = new();

    /// <summary>What it is applied to.</summary>
    public MathElement Argument { get; } = new();

    /// <inheritdoc />
    public override string GetText() => MathText.Join(Name.GetText(), MathText.Group(Argument.GetText()));
}

/// <summary>One row of a matrix (<c>m:mr</c>, §22.1.2.69).</summary>
public sealed class MathMatrixRow
{
    /// <summary>The cells of the row, in order.</summary>
    public IList<MathElement> Cells { get; } = [];
}

/// <summary>A grid of arguments (<c>m:m</c>, §22.1.2.60).</summary>
public sealed class MathMatrix : MathNode
{
    /// <summary>The rows, in order.</summary>
    public IList<MathMatrixRow> Rows { get; } = [];

    /// <inheritdoc />
    public override string GetText()
    {
        var builder = new StringBuilder("(");
        for (int row = 0; row < Rows.Count; row++)
        {
            if (row > 0)
                builder.Append("; ");
            for (int cell = 0; cell < Rows[row].Cells.Count; cell++)
            {
                if (cell > 0)
                    builder.Append(", ");
                builder.Append(Rows[row].Cells[cell].GetText());
            }
        }

        return builder.Append(')').ToString();
    }
}

/// <summary>A line above or below something (<c>m:bar</c>, §22.1.2.7).</summary>
public sealed class MathBar : MathNode
{
    /// <summary>What the line is drawn against.</summary>
    public MathElement Base { get; } = new();

    /// <summary>Which side of the base the line sits on.</summary>
    public MathEdge Position { get; set; }

    /// <inheritdoc />
    public override string GetText() => Base.GetText();
}

/// <summary>A mark above something (<c>m:acc</c>, §22.1.2.1).</summary>
public sealed class MathAccent : MathNode
{
    /// <summary>What the mark is drawn over.</summary>
    public MathElement Base { get; } = new();

    /// <summary>The mark itself, which defaults to a circumflex (<c>m:chr</c>, §22.1.2.20).</summary>
    public string Character { get; set; } = "\u0302";

    /// <inheritdoc />
    public override string GetText() => Base.GetText() + Character;
}

/// <summary>A character stretched over or under something (<c>m:groupChr</c>, §22.1.2.41).</summary>
public sealed class MathGroupCharacter : MathNode
{
    /// <summary>What the character is drawn against.</summary>
    public MathElement Base { get; } = new();

    /// <summary>The character, which defaults to an underbrace.</summary>
    public string Character { get; set; } = "\u23DF";

    /// <summary>Which side of the base it sits on.</summary>
    public MathEdge Position { get; set; }

    /// <inheritdoc />
    public override string GetText() => Base.GetText();
}

/// <summary>A group treated as one object for spacing and breaking (<c>m:box</c>, §22.1.2.13).</summary>
public sealed class MathBox : MathNode
{
    /// <summary>What the box holds.</summary>
    public MathElement Base { get; } = new();

    /// <inheritdoc />
    public override string GetText() => Base.GetText();
}
