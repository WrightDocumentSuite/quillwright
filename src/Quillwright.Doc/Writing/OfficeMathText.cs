using System.Text;
using System.Xml;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Recovers the readable text of an equation from the markup the model preserves it as.
/// </summary>
/// <remarks>
/// Equations are stored in a vocabulary of their own ([ECMA-376] part 1, section 22.1), which
/// the binary format has no equivalent for and the model therefore keeps verbatim rather than
/// interpreting. Writing nothing would leave a hole where the reader saw a formula, so the
/// run text inside the equation is pulled out and written as ordinary text. The layout is
/// lost — a fraction becomes its numerator next to its denominator — but the content is not.
/// </remarks>
internal static class OfficeMathText
{
    private const string MathNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    /// <summary>Returns <see langword="true"/> when a preserved fragment is an equation.</summary>
    /// <param name="xml">The verbatim markup.</param>
    public static bool IsEquation(string xml) =>
        xml.StartsWith("<m:oMath", StringComparison.Ordinal) ||
        xml.StartsWith("<m:oMathPara", StringComparison.Ordinal);

    /// <summary>
    /// The text of a modelled equation. What the file said comes first, because the markup an
    /// equation arrived as is a fuller record of it than the tree this library reads out of it.
    /// </summary>
    /// <param name="equation">The equation.</param>
    public static string? Flatten(Quillwright.Model.MathObject equation) =>
        (equation.OriginalXml is { } markup ? Extract(markup) : null) ?? equation.GetText();

    /// <summary>
    /// Extracts the text of an equation, or returns <see langword="null"/> when the fragment
    /// is not an equation or holds no text.
    /// </summary>
    /// <param name="xml">The verbatim markup.</param>
    public static string? Extract(string xml)
    {
        if (!IsEquation(xml))
            return null;

        var builder = new StringBuilder();
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Quillwright.Xml.XmlDefaults.ReaderSettings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element &&
                    reader.LocalName == "t" &&
                    reader.NamespaceURI == MathNamespace)
                {
                    builder.Append(reader.ReadElementContentAsString());
                }
            }
        }
        catch (XmlException)
        {
            return null;
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
