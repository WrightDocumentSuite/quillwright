using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Html;

/// <summary>The CSS2 marker names that can be represented by Word numbering levels.</summary>
internal static class HtmlListStyle
{
    public static string? ParseIdentifier(string value)
    {
        if (HtmlCssParser.Identifier(value) is not { } identifier)
            return null;

        return Canonical(HtmlCssParser.AsciiLower(identifier));
    }

    public static string? Canonical(string marker) => marker switch
    {
        "decimal" or "decimal-leading-zero" or "lower-roman" or "upper-roman"
            or "disc" or "circle" or "square" or "none" => marker,
        "lower-latin" or "lower-alpha" => "lower-latin",
        "upper-latin" or "upper-alpha" => "upper-latin",
        _ => null,
    };

    public static string FromLevel(NumberingLevel level)
    {
        if (level.Text.Length == 0 || level.Format == ListNumberFormat.None)
            return "none";

        return level.Format switch
        {
            ListNumberFormat.Decimal => "decimal",
            ListNumberFormat.DecimalZero => "decimal-leading-zero",
            ListNumberFormat.LowerRoman => "lower-roman",
            ListNumberFormat.UpperRoman => "upper-roman",
            ListNumberFormat.LowerLetter => "lower-latin",
            ListNumberFormat.UpperLetter => "upper-latin",
            ListNumberFormat.Bullet when level.RunFormat.FontAscii?.Equals(
                "Courier New", StringComparison.OrdinalIgnoreCase) == true => "circle",
            ListNumberFormat.Bullet when level.RunFormat.FontAscii?.Equals(
                "Wingdings", StringComparison.OrdinalIgnoreCase) == true => "square",
            ListNumberFormat.Bullet => "disc",
            _ => "decimal",
        };
    }

    public static void Apply(NumberingLevel level, string marker)
    {
        switch (marker)
        {
            case "decimal":
                SetOrdered(level, ListNumberFormat.Decimal);
                break;
            case "decimal-leading-zero":
                SetOrdered(level, ListNumberFormat.DecimalZero);
                break;
            case "lower-roman":
                SetOrdered(level, ListNumberFormat.LowerRoman);
                break;
            case "upper-roman":
                SetOrdered(level, ListNumberFormat.UpperRoman);
                break;
            case "lower-latin":
                SetOrdered(level, ListNumberFormat.LowerLetter);
                break;
            case "upper-latin":
                SetOrdered(level, ListNumberFormat.UpperLetter);
                break;
            case "disc" or "circle" or "square":
                SetBullet(level, marker);
                break;
            case "none":
                level.Text = string.Empty;
                break;
        }
    }

    private static void SetOrdered(NumberingLevel level, ListNumberFormat format)
    {
        level.Format = format;
        level.Text = $"%{level.Level + 1}.";
        level.RunFormat = RunFormat.Default;
    }

    private static void SetBullet(NumberingLevel level, string marker)
    {
        int templateLevel = marker switch
        {
            "circle" => 1,
            "square" => 2,
            _ => 0,
        };
        NumberingLevel template = ListTemplates.CreateLevel(ListTemplate.Bullet, templateLevel);
        level.Format = ListNumberFormat.Bullet;
        level.Text = template.Text;
        level.RunFormat = template.RunFormat;
    }
}
