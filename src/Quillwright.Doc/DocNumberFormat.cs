using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>
/// Converts between the model's numbering schemes and the numeric codes the binary format
/// names them with (<c>MSONFC</c>, [MS-OSHARED] 2.2.1.3).
/// </summary>
/// <remarks>
/// The same codes name the scheme of a list level and the scheme of a section's page
/// numbers, so the conversion lives here rather than in either of them.
/// </remarks>
internal static class DocNumberFormat
{
    /// <summary>The code for a scheme, or the code for plain digits when it has none.</summary>
    /// <param name="format">The scheme to encode.</param>
    public static byte Code(ListNumberFormat format) => format switch
    {
        ListNumberFormat.UpperRoman => 1,
        ListNumberFormat.LowerRoman => 2,
        ListNumberFormat.UpperLetter => 3,
        ListNumberFormat.LowerLetter => 4,
        ListNumberFormat.Ordinal => 5,
        ListNumberFormat.CardinalText => 6,
        ListNumberFormat.OrdinalText => 7,
        ListNumberFormat.DecimalZero => 22,
        ListNumberFormat.Bullet => 23,
        ListNumberFormat.None => 255,
        _ => 0,
    };

    /// <summary>The scheme a code names, or plain digits when the code is one this version does not know.</summary>
    /// <param name="code">The stored code.</param>
    public static ListNumberFormat Of(byte code) => code switch
    {
        1 => ListNumberFormat.UpperRoman,
        2 => ListNumberFormat.LowerRoman,
        3 => ListNumberFormat.UpperLetter,
        4 => ListNumberFormat.LowerLetter,
        5 => ListNumberFormat.Ordinal,
        6 => ListNumberFormat.CardinalText,
        7 => ListNumberFormat.OrdinalText,
        22 => ListNumberFormat.DecimalZero,
        23 => ListNumberFormat.Bullet,
        255 => ListNumberFormat.None,
        _ => ListNumberFormat.Decimal,
    };
}
