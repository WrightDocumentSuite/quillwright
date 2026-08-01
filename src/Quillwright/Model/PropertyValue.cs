using System.Globalization;

namespace Quillwright.Model;

/// <summary>What kind of value a document property carries (ISO/IEC 29500-1 §22.4).</summary>
public enum PropertyValueKind : byte
{
    /// <summary>No value at all.</summary>
    Empty = 0,

    /// <summary>Text (<c>vt:lpwstr</c>, <c>vt:lpstr</c>, <c>vt:bstr</c>).</summary>
    Text,

    /// <summary>A whole number (<c>vt:i1</c> through <c>vt:i8</c> and their unsigned twins).</summary>
    Integer,

    /// <summary>A real number (<c>vt:r4</c>, <c>vt:r8</c>, <c>vt:decimal</c>).</summary>
    Real,

    /// <summary>True or false (<c>vt:bool</c>).</summary>
    Boolean,

    /// <summary>A point in time (<c>vt:filetime</c>, <c>vt:date</c>).</summary>
    DateTime,

    /// <summary>A class identifier (<c>vt:clsid</c>).</summary>
    Guid,
}

/// <summary>
/// The value of a document property, kept as the type it was written in rather than flattened
/// to text.
/// </summary>
/// <remarks>
/// File properties are variants (ISO/IEC 29500-1 §22.4) and the same set of types appears
/// again, binary-encoded, in the property sets of a legacy document ([MS-OLEPS] 2.15). Only
/// the handful Word actually writes is modelled; a value of any other type is read as its text
/// so that nothing is lost from view, and written back unchanged.
/// </remarks>
public readonly record struct PropertyValue
{
    private readonly string? _text;
    private readonly long _integer;
    private readonly double _real;
    private readonly DateTimeOffset _timestamp;

    private PropertyValue(PropertyValueKind kind, string? text, long integer, double real, DateTimeOffset timestamp)
    {
        Kind = kind;
        _text = text;
        _integer = integer;
        _real = real;
        _timestamp = timestamp;
    }

    /// <summary>What kind of value this is.</summary>
    public PropertyValueKind Kind { get; }

    /// <summary>Whether there is no value.</summary>
    public bool IsEmpty => Kind == PropertyValueKind.Empty;

    /// <summary>Creates a text value.</summary>
    /// <param name="value">The text.</param>
    public static PropertyValue FromText(string value) =>
        new(PropertyValueKind.Text, value, 0, 0, default);

    /// <summary>Creates a whole-number value.</summary>
    /// <param name="value">The number.</param>
    public static PropertyValue FromInteger(long value) =>
        new(PropertyValueKind.Integer, null, value, 0, default);

    /// <summary>Creates a real-number value.</summary>
    /// <param name="value">The number.</param>
    public static PropertyValue FromReal(double value) =>
        new(PropertyValueKind.Real, null, 0, value, default);

    /// <summary>Creates a true-or-false value.</summary>
    /// <param name="value">The flag.</param>
    public static PropertyValue FromBoolean(bool value) =>
        new(PropertyValueKind.Boolean, null, value ? 1 : 0, 0, default);

    /// <summary>Creates a point in time.</summary>
    /// <param name="value">The moment.</param>
    public static PropertyValue FromDateTime(DateTimeOffset value) =>
        new(PropertyValueKind.DateTime, null, 0, 0, value);

    /// <summary>Creates a class identifier, keeping the spelling it was written in.</summary>
    /// <param name="value">The identifier as text, braces and all.</param>
    public static PropertyValue FromGuid(string value) =>
        new(PropertyValueKind.Guid, value, 0, 0, default);

    /// <summary>The text of a text value, or <see langword="null"/> for anything else.</summary>
    public string? AsText() => Kind is PropertyValueKind.Text or PropertyValueKind.Guid ? _text : null;

    /// <summary>The number of a whole-number value, or <see langword="null"/> for anything else.</summary>
    public long? AsInteger() => Kind == PropertyValueKind.Integer ? _integer : null;

    /// <summary>The number of a real-number value, or <see langword="null"/> for anything else.</summary>
    public double? AsReal() => Kind == PropertyValueKind.Real ? _real : null;

    /// <summary>The flag of a true-or-false value, or <see langword="null"/> for anything else.</summary>
    public bool? AsBoolean() => Kind == PropertyValueKind.Boolean ? _integer != 0 : null;

    /// <summary>The moment of a date value, or <see langword="null"/> for anything else.</summary>
    public DateTimeOffset? AsDateTime() => Kind == PropertyValueKind.DateTime ? _timestamp : null;

    /// <summary>The value rendered the way the file writes it.</summary>
    public override string ToString() => Kind switch
    {
        PropertyValueKind.Empty => string.Empty,
        PropertyValueKind.Text or PropertyValueKind.Guid => _text ?? string.Empty,
        PropertyValueKind.Integer => _integer.ToString(CultureInfo.InvariantCulture),
        PropertyValueKind.Real => _real.ToString("R", CultureInfo.InvariantCulture),
        PropertyValueKind.Boolean => _integer != 0 ? "true" : "false",
        _ => _timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
    };

    /// <summary>Wraps text as a property value.</summary>
    /// <param name="value">The text.</param>
    public static implicit operator PropertyValue(string value) => FromText(value);

    /// <summary>Wraps a whole number as a property value.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator PropertyValue(long value) => FromInteger(value);

    /// <summary>Wraps a real number as a property value.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator PropertyValue(double value) => FromReal(value);

    /// <summary>Wraps a flag as a property value.</summary>
    /// <param name="value">The flag.</param>
    public static implicit operator PropertyValue(bool value) => FromBoolean(value);

    /// <summary>Wraps a moment as a property value.</summary>
    /// <param name="value">The moment.</param>
    public static implicit operator PropertyValue(DateTimeOffset value) => FromDateTime(value);
}
