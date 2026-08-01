using System.Globalization;
using System.Text;

namespace Quillwright.Editing;

/// <summary>
/// Applies the three formatting switches a field result can carry: the general switch
/// (<c>\*</c>, §17.16.4.3), the numeric picture (<c>\#</c>, §17.16.4.2) and the date picture
/// (<c>\@</c>, §17.16.4.1).
/// </summary>
/// <remarks>
/// The switches that only tell an application what formatting to keep — <c>MERGEFORMAT</c>
/// and <c>CHARFORMAT</c> — say nothing about the text and are left to the consumer; the run
/// formatting of a field result is not rewritten here.
/// </remarks>
internal static class FieldFormat
{
    /// <summary>Formats a number the way a field result is formatted when nothing says otherwise.</summary>
    /// <param name="value">The value.</param>
    /// <param name="culture">Culture whose radix point and grouping are used.</param>
    public static string Number(double value, CultureInfo culture) =>
        Math.Abs(value - Math.Round(value)) < 1e-10
            ? Math.Round(value).ToString("0", culture)
            : value.ToString("0.##########", culture);

    /// <summary>Applies the general switch to a result.</summary>
    /// <param name="text">The result as it stands.</param>
    /// <param name="value">The result as a number, when it is one.</param>
    /// <param name="argument">The switch argument, or <see langword="null"/> for none.</param>
    /// <param name="culture">Culture the casing rules come from.</param>
    public static string General(string text, double? value, string? argument, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return text;

        return argument.Trim() switch
        {
            var casing when casing.Equals("Upper", StringComparison.OrdinalIgnoreCase) => text.ToUpper(culture),
            var casing when casing.Equals("Lower", StringComparison.OrdinalIgnoreCase) => text.ToLower(culture),
            var casing when casing.Equals("FirstCap", StringComparison.OrdinalIgnoreCase) => FirstCap(text, culture),
            var casing when casing.Equals("Caps", StringComparison.OrdinalIgnoreCase) => Caps(text, culture),
            var numbering when value is { } number => Numbered(numbering, number, text, culture),
            _ => text,
        };
    }

    /// <summary>
    /// Applies a numeric picture. Each picture item stands for one position of the result, so
    /// the digits are laid into the picture from the radix point outwards.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="picture">The switch argument.</param>
    /// <param name="culture">Culture whose radix point and grouping separator are used.</param>
    public static string Numeric(double value, string picture, CultureInfo culture)
    {
        // A picture may carry a form for positive, negative and zero results in turn. When it
        // does, the form spells out its own sign, so a minus in it is a character and not an
        // instruction to work one out.
        string[] forms = SplitForms(picture);
        string chosen = value switch
        {
            < 0 when forms.Length > 1 => forms[1],
            0 when forms.Length > 2 => forms[2],
            _ => forms[0],
        };

        return NumericPicture.Apply(value, chosen, signIsLiteral: forms.Length > 1, culture);
    }

    /// <summary>
    /// Turns a Word date picture into the equivalent .NET format string. The picture items of
    /// §17.16.4.1 are the ones .NET uses, with the meridian spelled out rather than lettered.
    /// </summary>
    /// <param name="picture">The switch argument.</param>
    public static string DatePattern(string picture)
    {
        var result = new StringBuilder(picture.Length);
        for (int i = 0; i < picture.Length; i++)
        {
            char c = picture[i];
            if (c == '\'')
            {
                int close = picture.IndexOf('\'', i + 1);
                if (close < 0)
                    break;
                result.Append(picture, i, close - i + 1);
                i = close;
                continue;
            }

            if (Meridian(picture, i) is { } meridian)
            {
                result.Append(meridian.Pattern);
                i += meridian.Length - 1;
                continue;
            }

            // A picture item .NET reads as a format specifier of its own has to be quoted, or
            // a stray letter in the picture would silently become part of the date.
            result.Append(char.IsLetter(c) && !"dMyHhmst".Contains(c, StringComparison.Ordinal) ? $"\\{c}" : c);
        }

        return result.ToString();
    }

    /// <summary>The meridian picture items, which Word spells out and .NET abbreviates.</summary>
    private static (string Pattern, int Length)? Meridian(string picture, int at)
    {
        if (Matches(picture, at, "AM/PM") || Matches(picture, at, "am/pm"))
            return ("tt", 5);
        if (Matches(picture, at, "A/P") || Matches(picture, at, "a/p"))
            return ("t", 3);
        return null;
    }

    private static bool Matches(string text, int at, string token) =>
        at + token.Length <= text.Length && text.AsSpan(at, token.Length).SequenceEqual(token);

    /// <summary>Splits a picture into its positive, negative and zero forms.</summary>
    private static string[] SplitForms(string picture)
    {
        var forms = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;

        foreach (char c in picture)
        {
            if (c == '\'')
                quoted = !quoted;

            if (c == ';' && !quoted)
            {
                forms.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        forms.Add(current.ToString());
        return [.. forms];
    }

    private static string FirstCap(string text, CultureInfo culture)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsLetter(text[i]))
                continue;
            return string.Concat(text.AsSpan(0, i), char.ToUpper(text[i], culture).ToString(), text.AsSpan(i + 1).ToString().ToLower(culture));
        }

        return text;
    }

    private static string Caps(string text, CultureInfo culture)
    {
        var result = new StringBuilder(text.Length);
        bool starting = true;
        foreach (char c in text)
        {
            result.Append(starting ? char.ToUpper(c, culture) : c);
            starting = !char.IsLetterOrDigit(c);
        }

        return result.ToString();
    }

    /// <summary>The switch arguments that renumber a numeric result rather than recase it.</summary>
    private static string Numbered(string argument, double value, string text, CultureInfo culture)
    {
        long whole = (long)Math.Round(value);
        return argument switch
        {
            _ when argument.Equals("Arabic", StringComparison.OrdinalIgnoreCase) => whole.ToString(culture),
            "ROMAN" => Roman(whole),
            _ when argument.Equals("roman", StringComparison.Ordinal) => Roman(whole).ToLowerInvariant(),
            _ when argument.Equals("Roman", StringComparison.OrdinalIgnoreCase) => Roman(whole),
            "ALPHABETIC" => Alphabetic(whole),
            _ when argument.Equals("alphabetic", StringComparison.Ordinal) => Alphabetic(whole).ToLowerInvariant(),
            _ when argument.Equals("Ordinal", StringComparison.OrdinalIgnoreCase) => Ordinal(whole, culture),
            _ when argument.Equals("Hex", StringComparison.OrdinalIgnoreCase) => whole.ToString("X", culture),
            _ => text,
        };
    }

    /// <summary>Roman numerals, in the subtractive form Word writes.</summary>
    private static string Roman(long value)
    {
        if (value is <= 0 or > 32767)
            return value.ToString(CultureInfo.InvariantCulture);

        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];

        var result = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            while (value >= values[i])
            {
                result.Append(symbols[i]);
                value -= values[i];
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// A letter repeated: 1 is A, 26 is Z, 27 is AA. Twenty-six is taken off until what is
    /// left names a letter, and the letter is repeated once for each time it was taken off.
    /// </summary>
    private static string Alphabetic(long value)
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);

        long repeats = (value - 1) / 26;
        char letter = (char)('A' + ((value - 1) % 26));
        return new string(letter, (int)Math.Min(repeats + 1, 1000));
    }

    private static string Ordinal(long value, CultureInfo culture)
    {
        string suffix = (value % 100, value % 10) switch
        {
            (11 or 12 or 13, _) => "th",
            (_, 1) => "st",
            (_, 2) => "nd",
            (_, 3) => "rd",
            _ => "th",
        };

        return value.ToString(culture) + suffix;
    }
}
