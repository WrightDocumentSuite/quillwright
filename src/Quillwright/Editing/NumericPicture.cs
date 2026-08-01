using System.Globalization;

namespace Quillwright.Editing;

/// <summary>
/// Lays a number into a numeric picture (ISO/IEC 29500-1 §17.16.4.2).
/// </summary>
/// <remarks>
/// A picture is positional rather than declarative: each item stands for one place of the
/// result, so the digits are fitted into it from the radix point outwards. That is why this
/// walks the whole-number part of the picture backwards — the last item is the units place,
/// whatever it happens to be.
/// </remarks>
internal static class NumericPicture
{
    /// <summary>Formats a value against one form of a picture.</summary>
    /// <param name="value">The value, sign and all.</param>
    /// <param name="picture">One form of the picture, without its <c>;</c> separators.</param>
    /// <param name="signIsLiteral">
    /// Whether <c>-</c> and <c>+</c> in the picture stand for themselves. They do when the
    /// picture spelled out a form of its own for negative results, and are sign items when it
    /// did not.
    /// </param>
    /// <param name="culture">Culture whose radix point and grouping separator are used.</param>
    public static string Apply(double value, string picture, bool signIsLiteral, CultureInfo culture)
    {
        (string whole, string fraction, bool hasPoint) = Split(picture);
        int places = Math.Min(Slots(fraction), 15);
        string digits = Math.Round(Math.Abs(value), places, MidpointRounding.AwayFromZero)
            .ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        int point = digits.IndexOf('.', StringComparison.Ordinal);
        string head = Whole(whole, point < 0 ? digits : digits[..point], Math.Sign(value), signIsLiteral, culture);
        if (!hasPoint)
            return head;

        string tail = Fraction(fraction, point < 0 ? string.Empty : digits[(point + 1)..]);
        return head + culture.NumberFormat.NumberDecimalSeparator + tail;
    }

    /// <summary>Splits a picture at its radix point, ignoring one inside quoted text.</summary>
    private static (string Whole, string Fraction, bool HasPoint) Split(string picture)
    {
        bool quoted = false;
        for (int i = 0; i < picture.Length; i++)
        {
            if (picture[i] == '\'')
                quoted = !quoted;
            else if (picture[i] == '.' && !quoted)
                return (picture[..i], picture[(i + 1)..], true);
        }

        return (picture, string.Empty, false);
    }

    /// <summary>How many decimal places the fractional part of a picture asks for.</summary>
    private static int Slots(string fraction)
    {
        int count = 0;
        foreach (char c in fraction)
        {
            if (c is '0' or '#' or 'x' or 'X')
                count++;
        }

        return count;
    }

    private static string Whole(string picture, string digits, int sign, bool signIsLiteral, CultureInfo culture)
    {
        var result = new List<char>(picture.Length + digits.Length);
        int next = digits.Length - 1;
        bool clipped = false;
        bool signed = false;

        for (int i = picture.Length - 1; i >= 0; i--)
        {
            char c = picture[i];
            if (c == '\'')
            {
                i = Literal(picture, i, result);
                continue;
            }

            if (!signIsLiteral && c is '-' or '+')
            {
                result.Add(Sign(c, sign));
                signed = true;
                continue;
            }

            switch (c)
            {
                case '0':
                    result.Add(next >= 0 ? digits[next--] : '0');
                    break;
                case '#':
                    result.Add(next >= 0 ? digits[next--] : ' ');
                    break;
                case 'x' or 'X':
                    if (next >= 0)
                        result.Add(digits[next--]);
                    clipped = true;
                    break;
                case ',':
                    if (next >= 0)
                        Append(result, culture.NumberFormat.NumberGroupSeparator);
                    break;
                default:
                    result.Add(c);
                    break;
            }
        }

        // Digits the picture left no room for are kept, because dropping them would show a
        // different number; only an "x" item says to drop them on purpose.
        while (!clipped && next >= 0)
            result.Add(digits[next--]);

        if (!signed && !signIsLiteral && sign < 0)
            result.Add('-');

        result.Reverse();
        return new string([.. result]);
    }

    private static string Fraction(string picture, string digits)
    {
        var result = new List<char>(picture.Length);
        int next = 0;

        for (int i = 0; i < picture.Length; i++)
        {
            char c = picture[i];
            switch (c)
            {
                case '\'':
                    i = Literal(picture, i, result, forwards: true);
                    break;
                case '0':
                    result.Add(next < digits.Length ? digits[next++] : '0');
                    break;
                case '#' or 'x' or 'X':
                    result.Add(next < digits.Length ? digits[next++] : ' ');
                    break;
                default:
                    result.Add(c);
                    break;
            }
        }

        return new string([.. result]);
    }

    private static char Sign(char item, int sign) => (item, sign) switch
    {
        ('-', < 0) => '-',
        ('-', _) => ' ',
        (_, < 0) => '-',
        (_, 0) => ' ',
        _ => '+',
    };

    /// <summary>
    /// Copies the text of a quoted picture item and returns the index of its other quote.
    /// Walking backwards means finding the opening quote and copying the text reversed.
    /// </summary>
    private static int Literal(string picture, int quote, List<char> result, bool forwards = false)
    {
        int other = forwards ? picture.IndexOf('\'', quote + 1) : picture.LastIndexOf('\'', quote - 1);
        if (other < 0)
            return forwards ? picture.Length : 0;

        if (forwards)
        {
            for (int i = quote + 1; i < other; i++)
                result.Add(picture[i]);
        }
        else
        {
            for (int i = quote - 1; i > other; i--)
                result.Add(picture[i]);
        }

        return other;
    }

    private static void Append(List<char> result, string separator)
    {
        for (int i = separator.Length - 1; i >= 0; i--)
            result.Add(separator[i]);
    }
}
