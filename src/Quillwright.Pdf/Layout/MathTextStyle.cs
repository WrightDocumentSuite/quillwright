using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Which face the characters of a maths run are drawn in.
/// </summary>
/// <remarks>
/// <para>
/// Mathematics has a convention older than the format: a variable is italic and everything else
/// — digits, operators, the names of functions and units — is upright. Word follows it by
/// default and lets a run say otherwise through <c>m:sty</c> (§22.1.2.111) or <c>m:nor</c>
/// (§22.1.2.74), which is how <c>sin</c> comes out upright inside an italic expression.
/// </para>
/// <para>
/// The model keeps a run's properties as the markup they arrived as, so the two settings are
/// read straight out of it. That is a narrow thing to do and it is deliberate: interpreting
/// those two elements is what an equation has to look right, and interpreting the rest of
/// <c>m:rPr</c> would be a second copy of the run-formatting reader.
/// </para>
/// </remarks>
internal readonly record struct MathTextStyle(bool Italic, bool Bold)
{
    /// <summary>The convention: letters lean, everything else stands up.</summary>
    public static MathTextStyle Default => new(Italic: true, Bold: false);

    /// <summary>What a run's own properties say about its face.</summary>
    /// <param name="properties">The verbatim <c>m:rPr</c>, or <see langword="null"/>.</param>
    public static MathTextStyle Of(string? properties)
    {
        if (properties is null)
            return Default;

        // Normal text is Word's way of saying "this is not mathematics, leave it alone".
        if (properties.Contains("<m:nor", StringComparison.Ordinal))
            return new MathTextStyle(Italic: false, Bold: false);

        return Value(properties, "m:sty") switch
        {
            "p" => new MathTextStyle(Italic: false, Bold: false),
            "b" => new MathTextStyle(Italic: false, Bold: true),
            "bi" => new MathTextStyle(Italic: true, Bold: true),
            _ => Default,
        };
    }

    /// <summary>The weight this style asks for, on top of whatever the surrounding run had.</summary>
    /// <param name="format">Formatting of the run the equation is anchored in.</param>
    public RunFormat Apply(RunFormat format) => Bold ? format with { Bold = true } : format;

    /// <summary>
    /// Cuts text into stretches that are all drawn in one face. A run that is not italic at all
    /// comes back whole; an italic one is cut wherever it crosses between letters and the rest,
    /// because a digit in the middle of a variable name is still a digit.
    /// </summary>
    /// <param name="text">The run's text.</param>
    public IEnumerable<(string Text, bool Italic)> Split(string text)
    {
        if (!Italic || text.Length == 0)
        {
            yield return (text, false);
            yield break;
        }

        int start = 0;
        bool leaning = char.IsLetter(text[0]);

        for (int i = 1; i < text.Length; i++)
        {
            if (char.IsLetter(text[i]) == leaning)
                continue;

            yield return (text[start..i], leaning);
            start = i;
            leaning = !leaning;
        }

        yield return (text[start..], leaning);
    }

    /// <summary>Reads the <c>m:val</c> of a named element out of preserved markup.</summary>
    private static string? Value(string markup, string element)
    {
        int at = markup.IndexOf('<' + element, StringComparison.Ordinal);
        if (at < 0)
            return null;

        int attribute = markup.IndexOf("val=\"", at, StringComparison.Ordinal);
        if (attribute < 0)
            return null;

        int start = attribute + "val=\"".Length;
        int end = markup.IndexOf('"', start);
        return end > start ? markup[start..end] : null;
    }
}
