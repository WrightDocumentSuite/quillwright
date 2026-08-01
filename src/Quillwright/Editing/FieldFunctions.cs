namespace Quillwright.Editing;

/// <summary>
/// The functions a formula field may call (ISO/IEC 29500-1 §17.16.3.4).
/// </summary>
/// <remarks>
/// Every one of them takes and returns a real number, because that is all a formula has: the
/// logical functions treat zero as false and anything else as true, and return one or zero.
/// A call this does not know, or one given the wrong number of arguments, is not a formula at
/// all and the field keeps the result it had.
/// </remarks>
internal static class FieldFunctions
{
    /// <summary>
    /// Whether the function takes a list, which is what decides that an argument naming
    /// several cells is expanded into their values rather than read as one expression.
    /// </summary>
    /// <param name="name">Name of the function, in any case.</param>
    public static bool TakesList(string name) =>
        name.ToUpperInvariant() is "AVERAGE" or "COUNT" or "MAX" or "MIN" or "PRODUCT" or "SUM";

    /// <summary>Calls a function.</summary>
    /// <param name="name">Name of the function, in any case.</param>
    /// <param name="arguments">The evaluated arguments, in order.</param>
    /// <exception cref="FormatException">The function is unknown, or the arguments do not suit it.</exception>
    public static double Call(string name, List<double> arguments)
    {
        string called = name.ToUpperInvariant();
        return called switch
        {
            "ABS" => Math.Abs(Only(called, arguments)),
            "AND" => Both(called, arguments) is var (x, y) && x != 0 && y != 0 ? 1 : 0,
            "AVERAGE" => NotEmpty(called, arguments).Average(),
            "COUNT" => arguments.Count,
            "FALSE" => None(called, arguments, 0),
            "IF" => Choose(called, arguments),
            "INT" => Math.Truncate(Only(called, arguments)),
            "MAX" => NotEmpty(called, arguments).Max(),
            "MIN" => NotEmpty(called, arguments).Min(),
            "MOD" => Modulo(Both(called, arguments)),
            "NOT" => Only(called, arguments) == 0 ? 1 : 0,
            "OR" => Both(called, arguments) is var (x, y) && (x != 0 || y != 0) ? 1 : 0,
            "PRODUCT" => Product(arguments),
            "ROUND" => Round(Both(called, arguments)),
            "SIGN" => Math.Sign(Only(called, arguments)),
            "SUM" => arguments.Sum(),
            "TRUE" => None(called, arguments, 1),
            _ => throw new FormatException($"'{name}' is not a function a formula field can call."),
        };
    }

    /// <summary>
    /// The remainder with the sign of the dividend, which is what §17.16.3.4 asks for:
    /// <c>MOD(-21,5)</c> is <c>-1</c>, not <c>4</c>.
    /// </summary>
    private static double Modulo((double Value, double Divisor) arguments)
    {
        (double value, double divisor) = arguments;
        return divisor == 0 ? throw new FormatException("MOD cannot divide by zero.") : value % divisor;
    }

    /// <summary>
    /// Rounding to a number of decimal places, or — when that number is negative — to the
    /// matching power of ten.
    /// </summary>
    private static double Round((double Value, double Places) arguments)
    {
        (double value, double places) = arguments;
        int digits = (int)Math.Floor(places);
        if (digits >= 0)
            return Math.Round(value, Math.Min(digits, 15), MidpointRounding.AwayFromZero);

        double scale = Math.Pow(10, -digits);
        return Math.Round(Math.Truncate(value) / scale, MidpointRounding.AwayFromZero) * scale;
    }

    private static double Choose(string name, List<double> arguments) =>
        arguments.Count == 3
            ? arguments[0] != 0 ? arguments[1] : arguments[2]
            : throw new FormatException($"{name} takes three arguments.");

    private static double Product(List<double> arguments)
    {
        double product = 1;
        foreach (double value in arguments)
            product *= value;
        return product;
    }

    private static double Only(string name, List<double> arguments) =>
        arguments.Count == 1 ? arguments[0] : throw new FormatException($"{name} takes one argument.");

    private static (double, double) Both(string name, List<double> arguments) =>
        arguments.Count == 2 ? (arguments[0], arguments[1]) : throw new FormatException($"{name} takes two arguments.");

    private static double None(string name, List<double> arguments, double value) =>
        arguments.Count == 0 ? value : throw new FormatException($"{name} takes no arguments.");

    private static List<double> NotEmpty(string name, List<double> arguments) =>
        arguments.Count > 0 ? arguments : throw new FormatException($"{name} needs something to work on.");
}
