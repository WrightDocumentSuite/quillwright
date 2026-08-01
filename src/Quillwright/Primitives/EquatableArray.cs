using System.Collections;

namespace Quillwright.Primitives;

/// <summary>
/// An immutable array with structural equality, so it can sit inside a record without
/// breaking value semantics. Arrays and collection types compare by reference, which would
/// stop two identical formats from ever comparing equal and defeat interning.
/// </summary>
/// <typeparam name="T">Element type, itself compared by value.</typeparam>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    /// <summary>Wraps an array. The array must not be mutated afterwards.</summary>
    public EquatableArray(T[]? items) => _items = items is { Length: > 0 } ? items : null;

    /// <summary>Wraps a sequence, copying it.</summary>
    public EquatableArray(IEnumerable<T> items) : this(items as T[] ?? [.. items])
    {
    }

    /// <summary>An array with no elements.</summary>
    public static EquatableArray<T> Empty => default;

    /// <summary>The number of elements.</summary>
    public int Count => _items?.Length ?? 0;

    /// <summary>Returns <see langword="true"/> when there are no elements.</summary>
    public bool IsEmpty => _items is null;

    /// <inheritdoc />
    public T this[int index] => _items is null ? throw new IndexOutOfRangeException() : _items[index];

    /// <summary>The elements as a span.</summary>
    public ReadOnlySpan<T> AsSpan() => _items;

    /// <inheritdoc />
    public bool Equals(EquatableArray<T> other) => AsSpan().SequenceEqual(other.AsSpan());

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        if (_items is null)
            return 0;

        var hash = new HashCode();
        hash.Add(_items.Length);
        foreach (T item in _items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    /// <summary>Compares two arrays element by element.</summary>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    /// <summary>Compares two arrays element by element.</summary>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <summary>Wraps an array.</summary>
    public static implicit operator EquatableArray<T>(T[]? items) => new(items);

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
