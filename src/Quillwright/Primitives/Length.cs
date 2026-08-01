using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Quillwright.Primitives;

/// <summary>
/// A measurement stored in twips (one twentieth of a point, 1/1440 inch) — the unit
/// WordprocessingML uses for almost everything on the page.
/// </summary>
/// <remarks>
/// OOXML expresses lengths in at least five units depending on the attribute: twips for
/// indents and page geometry, half-points for font size, eighths of a point for border
/// widths, EMUs for drawings and hundredths of a millimetre in a few places. One value type
/// that converts between all of them keeps the unit out of every signature and out of the
/// caller's head. Formatting writes twips, which is what the majority of attributes take.
/// </remarks>
public readonly struct Length :
    IEquatable<Length>,
    IComparable<Length>,
    ISpanFormattable,
    IUtf8SpanFormattable,
    ISpanParsable<Length>
{
    /// <summary>Twips in one point.</summary>
    public const int TwipsPerPoint = 20;

    /// <summary>Twips in one inch.</summary>
    public const int TwipsPerInch = 1440;

    /// <summary>English Metric Units in one twip.</summary>
    public const int EmuPerTwip = 635;

    /// <summary>English Metric Units in one inch.</summary>
    public const int EmuPerInch = 914_400;

    private const double TwipsPerCentimeter = TwipsPerInch / 2.54;

    private readonly int _twips;

    private Length(int twips) => _twips = twips;

    /// <summary>A zero-length measurement.</summary>
    public static Length Zero => default;

    /// <summary>The smallest representable measurement.</summary>
    public static Length MinValue => new(int.MinValue);

    /// <summary>The largest representable measurement.</summary>
    public static Length MaxValue => new(int.MaxValue);

    /// <summary>Creates a measurement from twips (1/1440 inch).</summary>
    public static Length FromTwips(int twips) => new(twips);

    /// <summary>Creates a measurement from points.</summary>
    public static Length FromPoints(double points) => new(Round(points * TwipsPerPoint));

    /// <summary>Creates a measurement from half-points, the unit of <c>w:sz</c>.</summary>
    public static Length FromHalfPoints(int halfPoints) => new(halfPoints * (TwipsPerPoint / 2));

    /// <summary>Creates a measurement from eighths of a point, the unit of border widths.</summary>
    public static Length FromEighthPoints(int eighthPoints) => new(Round(eighthPoints * (TwipsPerPoint / 8.0)));

    /// <summary>Creates a measurement from inches.</summary>
    public static Length FromInches(double inches) => new(Round(inches * TwipsPerInch));

    /// <summary>Creates a measurement from centimetres.</summary>
    public static Length FromCentimeters(double centimeters) => new(Round(centimeters * TwipsPerCentimeter));

    /// <summary>Creates a measurement from millimetres.</summary>
    public static Length FromMillimeters(double millimeters) => new(Round(millimeters * TwipsPerCentimeter / 10));

    /// <summary>Creates a measurement from English Metric Units, the unit of DrawingML.</summary>
    public static Length FromEmu(long emu) => new(Round(emu / (double)EmuPerTwip));

    /// <summary>Creates a measurement from pixels at the given resolution.</summary>
    public static Length FromPixels(double pixels, double dotsPerInch = 96) => new(Round(pixels * TwipsPerInch / dotsPerInch));

    /// <summary>The measurement in twips.</summary>
    public int Twips => _twips;

    /// <summary>The measurement in points.</summary>
    public double Points => _twips / (double)TwipsPerPoint;

    /// <summary>The measurement rounded to half-points, the unit of <c>w:sz</c>.</summary>
    public int HalfPoints => Round(_twips / (TwipsPerPoint / 2.0));

    /// <summary>The measurement rounded to eighths of a point, the unit of border widths.</summary>
    public int EighthPoints => Round(_twips / (TwipsPerPoint / 8.0));

    /// <summary>The measurement in inches.</summary>
    public double Inches => _twips / (double)TwipsPerInch;

    /// <summary>The measurement in centimetres.</summary>
    public double Centimeters => _twips / TwipsPerCentimeter;

    /// <summary>The measurement in millimetres.</summary>
    public double Millimeters => _twips * 10 / TwipsPerCentimeter;

    /// <summary>The measurement in English Metric Units, the unit of DrawingML.</summary>
    public long Emu => (long)_twips * EmuPerTwip;

    /// <summary>The measurement in pixels at the given resolution.</summary>
    public double ToPixels(double dotsPerInch = 96) => _twips * dotsPerInch / TwipsPerInch;

    /// <summary>Adds two measurements.</summary>
    public static Length operator +(Length left, Length right) => new(left._twips + right._twips);

    /// <summary>Subtracts one measurement from another.</summary>
    public static Length operator -(Length left, Length right) => new(left._twips - right._twips);

    /// <summary>Negates a measurement.</summary>
    public static Length operator -(Length value) => new(-value._twips);

    /// <summary>Scales a measurement.</summary>
    public static Length operator *(Length value, double factor) => new(Round(value._twips * factor));

    /// <summary>Scales a measurement.</summary>
    public static Length operator *(double factor, Length value) => value * factor;

    /// <summary>Divides a measurement.</summary>
    public static Length operator /(Length value, double divisor) => new(Round(value._twips / divisor));

    /// <summary>Compares two measurements.</summary>
    public static bool operator <(Length left, Length right) => left._twips < right._twips;

    /// <summary>Compares two measurements.</summary>
    public static bool operator >(Length left, Length right) => left._twips > right._twips;

    /// <summary>Compares two measurements.</summary>
    public static bool operator <=(Length left, Length right) => left._twips <= right._twips;

    /// <summary>Compares two measurements.</summary>
    public static bool operator >=(Length left, Length right) => left._twips >= right._twips;

    /// <summary>Compares two measurements for equality.</summary>
    public static bool operator ==(Length left, Length right) => left._twips == right._twips;

    /// <summary>Compares two measurements for inequality.</summary>
    public static bool operator !=(Length left, Length right) => left._twips != right._twips;

    /// <inheritdoc />
    public bool Equals(Length other) => _twips == other._twips;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Length other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _twips;

    /// <inheritdoc />
    public int CompareTo(Length other) => _twips.CompareTo(other._twips);

    /// <inheritdoc />
    public override string ToString() => _twips.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        _twips.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        _twips.TryFormat(destination, out charsWritten, format, provider ?? CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        _twips.TryFormat(utf8Destination, out bytesWritten, format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>Parses a twips value.</summary>
    public static Length Parse(string s, IFormatProvider? provider = null) => Parse(s.AsSpan(), provider);

    /// <summary>Parses a twips value.</summary>
    public static Length Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
        TryParse(s, provider, out Length result)
            ? result
            : throw new FormatException($"'{s}' is not a valid twips measurement.");

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Length result) =>
        TryParse(s.AsSpan(), provider, out result);

    /// <inheritdoc />
    /// <remarks>
    /// A bare number is twips, which is what the majority of attributes take. A number
    /// carrying one of the six unit identifiers of <c>ST_UniversalMeasure</c> (ISO/IEC
    /// 29500-1 §22.9.2.15) is the length it names: <c>ST_TwipsMeasure</c> and its signed
    /// counterpart are unions that admit either spelling, and a Strict producer writes
    /// <c>36pt</c> where a Transitional one writes <c>720</c>.
    /// </remarks>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Length result)
    {
        ReadOnlySpan<char> text = s.Trim();
        if (HasUnit(text))
            return TryParseUnit(text[..^2], text[^2..], out result);

        // Some producers write a decimal even where the schema says integer.
        if (double.TryParse(text, NumberStyles.Float, provider ?? CultureInfo.InvariantCulture, out double value) &&
            value is >= int.MinValue and <= int.MaxValue)
        {
            result = new Length(Round(value));
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Whether a value ends in one of the unit identifiers of <c>ST_UniversalMeasure</c>
    /// (§22.9.2.15) rather than being the bare number the same attribute also accepts.
    /// </summary>
    /// <param name="value">The attribute value as it was written.</param>
    /// <remarks>
    /// Which unit a bare number is in depends on the attribute — twips for an indent,
    /// half-points for a font size — so a caller that measures in something other than twips
    /// has to ask this before deciding how to read the number.
    /// </remarks>
    public static bool HasUnit(ReadOnlySpan<char> value)
    {
        ReadOnlySpan<char> text = value.Trim();

        // Every identifier is two characters, and a value that is nothing but a unit is not a
        // measurement at all.
        return text.Length > 2 && text[^2..] is "mm" or "cm" or "in" or "pt" or "pc" or "pi";
    }

    private static bool TryParseUnit(ReadOnlySpan<char> number, ReadOnlySpan<char> unit, out Length result)
    {
        result = default;

        // The grammar spells the number with a full stop whatever the reader's culture is.
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return false;

        double twips = unit switch
        {
            "mm" => value * TwipsPerCentimeter / 10,
            "cm" => value * TwipsPerCentimeter,
            "in" => value * TwipsPerInch,
            "pt" => value * TwipsPerPoint,
            // A pica is twelve points, and the standard gives it two spellings.
            _ => value * TwipsPerPoint * 12,
        };

        if (double.IsNaN(twips) || twips is < int.MinValue or > int.MaxValue)
            return false;

        result = new Length(Round(twips));
        return true;
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
