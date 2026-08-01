using System.Globalization;
using System.Text;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Rendering;

/// <summary>A resolved list label and the counter value used for its current level.</summary>
internal readonly record struct NumberLabel(string Text, int Value, NumberingLevel Level);

/// <summary>Counts Word list instances in document order, including starts and restarts.</summary>
internal sealed class NumberingCounter
{
    private const int Depth = 9;

    private readonly NumberingDefinitions _numbering;
    private readonly Dictionary<int, int[]> _counters = [];
    private readonly Dictionary<int, bool[]> _started = [];

    public NumberingCounter(NumberingDefinitions numbering) => _numbering = numbering;

    public NumberLabel? Next(ParagraphFormat format) => Resolve(format, advance: true);

    public NumberLabel? Peek(ParagraphFormat format) => Resolve(format, advance: false);

    private NumberLabel? Resolve(ParagraphFormat format, bool advance)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.NumberingId is not { } id || id == 0)
            return null;

        int level = Math.Clamp(format.NumberingLevel ?? 0, 0, Depth - 1);
        if (_numbering.ResolveLevel(id, level) is not { } definition)
            return null;

        int[] counters = Counters(id);
        bool[] started = Started(id);
        int[] savedCounters = advance ? [] : [.. counters];
        bool[] savedStarted = advance ? [] : [.. started];

        Advance(id, level);
        int value = counters[level];
        var label = new NumberLabel(Render(id, definition), value, definition);

        if (!advance)
        {
            Array.Copy(savedCounters, counters, counters.Length);
            Array.Copy(savedStarted, started, started.Length);
        }

        return label;
    }

    private void Advance(int id, int level)
    {
        int[] counters = Counters(id);
        bool[] started = Started(id);
        counters[level] = started[level] ? counters[level] + 1 : Start(id, level);
        started[level] = true;

        for (int deeper = level + 1; deeper < Depth; deeper++)
        {
            if (Restarts(id, deeper, level))
                started[deeper] = false;
        }
    }

    private bool Restarts(int id, int deeper, int changed)
    {
        int? after = _numbering.ResolveLevel(id, deeper)?.RestartAfter;
        return after != 0 && changed + 1 <= (after ?? deeper);
    }

    private int Start(int id, int level)
    {
        NumberingInstance? instance = _numbering.Instances.FirstOrDefault(candidate => candidate.Id == id);
        NumberingLevelOverride? over = instance?.Overrides.FirstOrDefault(candidate => candidate.Level == level);
        return over?.StartOverride ?? _numbering.ResolveLevel(id, level)?.Start ?? 1;
    }

    private string Render(int id, NumberingLevel definition)
    {
        if (definition.Format == ListNumberFormat.None)
            return string.Empty;
        if (definition.Format == ListNumberFormat.Bullet || !definition.Text.Contains('%', StringComparison.Ordinal))
            return definition.Text;

        var result = new StringBuilder(definition.Text.Length + 8);
        for (int i = 0; i < definition.Text.Length; i++)
        {
            char c = definition.Text[i];
            if (c != '%' || i + 1 >= definition.Text.Length || definition.Text[i + 1] is < '1' or > '9')
            {
                result.Append(c);
                continue;
            }

            int referenced = definition.Text[++i] - '1';
            ListNumberFormat format = definition.IsLegal
                ? ListNumberFormat.Decimal
                : _numbering.ResolveLevel(id, referenced)?.Format ?? ListNumberFormat.Decimal;
            int value = Started(id)[referenced] ? Counters(id)[referenced] : Start(id, referenced);
            result.Append(NumberFormatter.Format(value, format));
        }

        return result.ToString();
    }

    private int[] Counters(int id)
    {
        if (!_counters.TryGetValue(id, out int[]? values))
            _counters[id] = values = new int[Depth];
        return values;
    }

    private bool[] Started(int id)
    {
        if (!_started.TryGetValue(id, out bool[]? values))
            _started[id] = values = new bool[Depth];
        return values;
    }
}

/// <summary>Formats the numbering schemes shared by lists, notes and page fields.</summary>
internal static class NumberFormatter
{
    private static readonly (int Value, string Numeral)[] RomanNumerals =
    [
        (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"),
        (100, "c"), (90, "xc"), (50, "l"), (40, "xl"),
        (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i"),
    ];

    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens =
        ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    private static readonly string[] OrdinalOnes =
    [
        "Zeroth", "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth",
        "Eleventh", "Twelfth", "Thirteenth", "Fourteenth", "Fifteenth", "Sixteenth", "Seventeenth", "Eighteenth",
        "Nineteenth",
    ];

    private static readonly string[] OrdinalTens =
        ["", "", "Twentieth", "Thirtieth", "Fortieth", "Fiftieth", "Sixtieth", "Seventieth", "Eightieth", "Ninetieth"];

    private const string RussianLetters = "абвгдежзиклмнопрстуфхцчшщэюя";

    public static string Format(int value, ListNumberFormat format) => format switch
    {
        ListNumberFormat.None => string.Empty,
        ListNumberFormat.DecimalZero => value.ToString("00", CultureInfo.InvariantCulture),
        ListNumberFormat.LowerRoman => Roman(value),
        ListNumberFormat.UpperRoman => Roman(value).ToUpperInvariant(),
        ListNumberFormat.LowerLetter => Alphabet(value, "abcdefghijklmnopqrstuvwxyz"),
        ListNumberFormat.UpperLetter => Alphabet(value, "ABCDEFGHIJKLMNOPQRSTUVWXYZ"),
        ListNumberFormat.RussianLower => Alphabet(value, RussianLetters),
        ListNumberFormat.RussianUpper => Alphabet(value, RussianLetters).ToUpperInvariant(),
        ListNumberFormat.Ordinal => Ordinal(value),
        ListNumberFormat.CardinalText => Cardinal(value),
        ListNumberFormat.OrdinalText => OrdinalText(value),
        ListNumberFormat.DecimalEnclosedCircle => Circled(value),
        ListNumberFormat.DecimalFullWidth => FullWidth(value),
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private static string Roman(int value)
    {
        if (value is <= 0 or > 3999)
            return value.ToString(CultureInfo.InvariantCulture);

        var builder = new StringBuilder(15);
        foreach ((int amount, string numeral) in RomanNumerals)
        {
            while (value >= amount)
            {
                builder.Append(numeral);
                value -= amount;
            }
        }

        return builder.ToString();
    }

    private static string Alphabet(int value, string alphabet)
    {
        if (value <= 0)
            return value.ToString(CultureInfo.InvariantCulture);
        int index = (value - 1) % alphabet.Length;
        int repeat = ((value - 1) / alphabet.Length) + 1;
        return new string(alphabet[index], repeat);
    }

    private static string Ordinal(int value)
    {
        string suffix = (value % 100) is >= 11 and <= 13
            ? "th"
            : (value % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return value.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static string Cardinal(int value)
    {
        if (value is < 0 or > 999_999)
            return value.ToString(CultureInfo.InvariantCulture);
        var builder = new StringBuilder(48);
        Spell(value, builder, ordinal: false);
        return builder.ToString();
    }

    private static string OrdinalText(int value)
    {
        if (value is < 0 or > 999_999)
            return Ordinal(value);
        var builder = new StringBuilder(48);
        Spell(value, builder, ordinal: true);
        return builder.ToString();
    }

    private static void Spell(int value, StringBuilder builder, bool ordinal)
    {
        if (value >= 1000)
        {
            int thousands = value / 1000;
            int rest = value % 1000;
            Spell(thousands, builder, ordinal: false);
            builder.Append(rest == 0 && ordinal ? " Thousandth" : " Thousand");
            if (rest == 0)
                return;
            builder.Append(' ');
            Spell(rest, builder, ordinal);
            return;
        }

        if (value >= 100)
        {
            int hundreds = value / 100;
            int rest = value % 100;
            builder.Append(Ones[hundreds]);
            builder.Append(rest == 0 && ordinal ? " Hundredth" : " Hundred");
            if (rest == 0)
                return;
            builder.Append(' ');
            Spell(rest, builder, ordinal);
            return;
        }

        if (value >= 20)
        {
            int tens = value / 10;
            int rest = value % 10;
            if (rest == 0)
            {
                builder.Append(ordinal ? OrdinalTens[tens] : Tens[tens]);
                return;
            }

            builder.Append(Tens[tens]).Append('-');
            builder.Append(ordinal ? OrdinalOnes[rest] : Ones[rest]);
            return;
        }

        builder.Append(ordinal ? OrdinalOnes[value] : Ones[value]);
    }

    private static string Circled(int value) => value switch
    {
        >= 1 and <= 20 => ((char)('①' + value - 1)).ToString(),
        0 => "⓪",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private static string FullWidth(int value)
    {
        string digits = value.ToString(CultureInfo.InvariantCulture);
        return string.Concat(digits.Select(c => char.IsAsciiDigit(c) ? (char)('０' + c - '0') : c));
    }
}
