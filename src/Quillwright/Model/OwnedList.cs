using System.Collections;

namespace Quillwright.Model;

/// <summary>
/// A list that keeps an ownership link on its items: adding sets the owner, removing clears
/// it. Table rows and cells use it so that <c>cell.Row.Table.Document</c> is always true.
/// </summary>
/// <typeparam name="TItem">Element type.</typeparam>
public sealed class OwnedList<TItem> : IList<TItem>
    where TItem : class
{
    private readonly List<TItem> _items = [];
    private readonly Action<TItem> _attach;
    private readonly Action<TItem> _detach;

    internal OwnedList(Action<TItem> attach, Action<TItem> detach)
    {
        _attach = attach;
        _detach = detach;
    }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public TItem this[int index]
    {
        get => _items[index];
        set
        {
            _detach(_items[index]);
            _attach(value);
            _items[index] = value;
        }
    }

    /// <inheritdoc />
    public void Add(TItem item)
    {
        _attach(item);
        _items.Add(item);
    }

    /// <summary>Adds several items in order.</summary>
    /// <param name="items">The items to add.</param>
    public void AddRange(IEnumerable<TItem> items)
    {
        foreach (TItem item in items)
            Add(item);
    }

    /// <inheritdoc />
    public void Insert(int index, TItem item)
    {
        _attach(item);
        _items.Insert(index, item);
    }

    /// <inheritdoc />
    public bool Remove(TItem item)
    {
        if (!_items.Remove(item))
            return false;
        _detach(item);
        return true;
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        _detach(_items[index]);
        _items.RemoveAt(index);
    }

    /// <inheritdoc />
    public void Clear()
    {
        foreach (TItem item in _items)
            _detach(item);
        _items.Clear();
    }

    /// <inheritdoc />
    public bool Contains(TItem item) => _items.Contains(item);

    /// <inheritdoc />
    public int IndexOf(TItem item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void CopyTo(TItem[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public List<TItem>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<TItem> IEnumerable<TItem>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
