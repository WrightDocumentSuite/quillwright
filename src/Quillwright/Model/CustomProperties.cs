using System.Collections;

namespace Quillwright.Model;

/// <summary>
/// One custom property of a document (<c>docProps/custom.xml</c>, ISO/IEC 29500-1 §22.3.2.2).
/// </summary>
public sealed class CustomProperty
{
    /// <summary>Creates a property.</summary>
    /// <param name="name">Name the property is known by.</param>
    /// <param name="value">The value it carries.</param>
    public CustomProperty(string name, PropertyValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        Value = value;
    }

    /// <summary>Name the property is known by.</summary>
    public string Name { get; }

    /// <summary>The value it carries.</summary>
    public PropertyValue Value { get; set; }

    /// <summary>
    /// Bookmark the value is a cache of (<c>linkTarget</c>). When set, a consumer refreshes
    /// the value from that bookmark on save rather than trusting what is stored.
    /// </summary>
    public string? LinkTarget { get; set; }
}

/// <summary>
/// The custom properties of a document: the free-form metadata a document management system
/// stores alongside the fixed core fields.
/// </summary>
/// <remarks>
/// A legacy <c>.doc</c> keeps the same information in the user-defined section of its
/// <c>DocumentSummaryInformation</c> property set ([MS-OLEPS] 2.21), so both formats read into
/// and write out of this one collection.
/// </remarks>
public sealed class CustomPropertyCollection : IReadOnlyList<CustomProperty>
{
    /// <summary>
    /// The format identifier Word writes on every custom property. It names the OLE property
    /// set the properties correspond to, and is the same for every document.
    /// </summary>
    public const string FormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";

    private readonly List<CustomProperty> _items = [];

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public CustomProperty this[int index] => _items[index];

    /// <summary>The property of a given name, or <see langword="null"/> when there is none.</summary>
    /// <param name="name">Name to look for; matching ignores case, as Word does.</param>
    public CustomProperty? this[string name] =>
        _items.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a property, or replaces the value of one that is already there.</summary>
    /// <param name="name">Name the property is known by.</param>
    /// <param name="value">The value it carries.</param>
    public CustomProperty Set(string name, PropertyValue value)
    {
        if (this[name] is { } existing)
        {
            existing.Value = value;
            return existing;
        }

        var added = new CustomProperty(name, value);
        _items.Add(added);
        return added;
    }

    /// <summary>Removes a property.</summary>
    /// <param name="name">Name of the property to remove.</param>
    /// <returns><see langword="true"/> when one was found.</returns>
    public bool Remove(string name) =>
        _items.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>Removes every property.</summary>
    public void Clear() => _items.Clear();

    internal void Add(CustomProperty property) => _items.Add(property);

    /// <inheritdoc />
    public IEnumerator<CustomProperty> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
