using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>Compatibility facade over the core number formatter shared by exporters.</summary>
internal static class NumberFormatter
{
    public static string Format(int value, ListNumberFormat format) =>
        Quillwright.Rendering.NumberFormatter.Format(value, format);
}
