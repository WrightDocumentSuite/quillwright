using System.Text;

namespace Quillwright.Model;

/// <summary>
/// One node of an equation (ISO/IEC 29500-1 §22.1).
/// </summary>
/// <remarks>
/// Office Math is a vocabulary of 124 elements, most of which describe how a formula is drawn
/// rather than what it says. The tree here models the structures that carry meaning — the
/// fractions, radicals, scripts, sums and delimiters — and keeps the rest verbatim, exactly as
/// pictures and drawings are kept. An equation nothing touched is written back byte for byte.
/// </remarks>
public abstract class MathNode
{
    /// <summary>The node as a line of text, with the structure spelled out rather than drawn.</summary>
    public abstract string GetText();

    /// <summary>
    /// The object's control properties (<c>m:ctrlPr</c>, §22.1.2.23), kept verbatim.
    /// </summary>
    /// <remarks>
    /// Every object in the vocabulary ends its properties with a <c>ctrlPr</c>, which carries
    /// the character formatting of the character the object is drawn around: its font, its
    /// size, whether it is italic, whether a line may break at it. None of that is structure,
    /// so the model does not interpret it — but all of it is visible, so it is carried rather
    /// than dropped when the markup is regenerated. Which properties are not carried is listed
    /// in the equations guide, <c>docs/math.md</c>.
    /// </remarks>
    public string? ControlPropertiesXml { get; set; }
}

/// <summary>
/// An argument of a mathematical object (<c>m:e</c>, §22.1.2.32), and the content of an
/// equation itself: an ordered list of nodes.
/// </summary>
public sealed class MathElement
{
    /// <summary>Creates an empty argument.</summary>
    public MathElement()
    {
    }

    /// <summary>Creates an argument holding the given nodes.</summary>
    /// <param name="nodes">The nodes, in order.</param>
    public MathElement(params MathNode[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        foreach (MathNode node in nodes)
            Nodes.Add(node);
    }

    /// <summary>Creates an argument holding one run of text.</summary>
    /// <param name="text">The text.</param>
    public static MathElement Of(string text) => new(new MathRun(text));

    /// <summary>The nodes, in order.</summary>
    public IList<MathNode> Nodes { get; } = [];

    /// <summary>Whether the argument holds nothing.</summary>
    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>The argument as a line of text.</summary>
    public string GetText()
    {
        if (Nodes.Count == 1)
            return Nodes[0].GetText();

        var builder = new StringBuilder();
        foreach (MathNode node in Nodes)
            builder.Append(node.GetText());
        return builder.ToString();
    }
}

/// <summary>A stretch of text inside an equation (<c>m:r</c>, §22.1.2.87).</summary>
public sealed class MathRun : MathNode
{
    /// <summary>Creates a run.</summary>
    /// <param name="text">The text it carries.</param>
    public MathRun(string text = "") => Text = text;

    /// <summary>The text.</summary>
    public string Text { get; set; }

    /// <summary>
    /// The run's own properties (<c>m:rPr</c> and <c>w:rPr</c>), kept verbatim because they
    /// are ordinary character formatting plus a handful of settings about how the glyphs are
    /// chosen, and the model has nowhere to put the latter.
    /// </summary>
    public string? PropertiesXml { get; set; }

    /// <inheritdoc />
    public override string GetText() => Text;
}

/// <summary>
/// Markup inside an equation that this version does not model — a phantom, a border box, an
/// array — kept verbatim so that saving does not lose it.
/// </summary>
public sealed class RawMath : MathNode
{
    /// <summary>Creates a preserved fragment.</summary>
    /// <param name="xml">The verbatim markup.</param>
    /// <param name="text">The text found inside it, for reading the equation as a line.</param>
    public RawMath(string xml, string text = "")
    {
        Xml = xml;
        Text = text;
    }

    /// <summary>The verbatim markup.</summary>
    public string Xml { get; }

    /// <summary>The text found inside the fragment.</summary>
    public string Text { get; }

    /// <inheritdoc />
    public override string GetText() => Text;
}

/// <summary>Puts brackets round a part of an equation when reading it as a line needs them.</summary>
internal static class MathText
{
    /// <summary>
    /// A single symbol needs no brackets and neither does one that already has them; anything
    /// else does, or <c>1+2/3</c> would read as a third added to one.
    /// </summary>
    /// <param name="text">The text of the part.</param>
    public static string Group(string text)
    {
        if (text.Length <= 1 || Bracketed(text))
            return text;

        foreach (char c in text)
        {
            if (!char.IsLetterOrDigit(c) && c != '.')
                return "(" + text + ")";
        }

        return text;
    }

    /// <summary>
    /// Joins two parts of an equation, keeping them apart when running them together would
    /// make one word of them: a sum over a range is <c>∑_(i=1)^n i</c>, not <c>∑_(i=1)^ni</c>.
    /// </summary>
    /// <param name="left">What comes first.</param>
    /// <param name="right">What follows it.</param>
    public static string Join(string left, string right) =>
        left.Length > 0 && right.Length > 0 && Wordish(left[^1]) && Wordish(right[0])
            ? left + " " + right
            : left + right;

    private static bool Wordish(char c) => char.IsLetterOrDigit(c) || c == '.';

    private static bool Bracketed(string text)
    {
        if (text.Length < 2 || text[0] is not ('(' or '[' or '{'))
            return false;

        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '(' or '[' or '{')
                depth++;
            else if (text[i] is ')' or ']' or '}' && --depth == 0)
                return i == text.Length - 1;
        }

        return false;
    }
}
