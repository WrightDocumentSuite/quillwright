using System.Collections;

namespace Quillwright.Model;

/// <summary>
/// A block-level element: a paragraph, a table, a block-level content control or preserved
/// markup. Blocks are the only things a body, a cell, a header or a note can contain.
/// </summary>
public abstract class Block
{
    /// <summary>The container this block belongs to, or <see langword="null"/> when detached.</summary>
    public BlockContainer? Parent { get; internal set; }

    /// <summary>The document this block belongs to, or <see langword="null"/> when detached.</summary>
    public WordDocument? Document => Parent?.Document;

    /// <summary>The text of this block, with paragraphs separated by newlines.</summary>
    public abstract string GetText();

    /// <summary>Returns an independent copy of this block, not attached to any container.</summary>
    public abstract Block Clone();
}

/// <summary>Markup at block level that the model does not interpret, kept verbatim.</summary>
public sealed class RawBlock : Block
{
    /// <summary>Creates a preserved block.</summary>
    /// <param name="xml">The verbatim markup.</param>
    public RawBlock(string xml) => Xml = xml;

    /// <summary>The verbatim markup.</summary>
    public string Xml { get; }

    /// <inheritdoc />
    public override string GetText() => string.Empty;

    /// <inheritdoc />
    public override Block Clone() => new RawBlock(Xml);
}

/// <summary>
/// The ordered list of blocks inside a container. Adding a block re-parents it, which keeps
/// <see cref="Block.Parent"/> and <see cref="Block.Document"/> true at all times.
/// </summary>
public sealed class BlockCollection : IList<Block>
{
    private readonly List<Block> _items = [];
    private readonly BlockContainer _owner;

    internal BlockCollection(BlockContainer owner) => _owner = owner;

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public Block this[int index]
    {
        get => _items[index];
        set
        {
            Detach(_items[index]);
            _items[index] = Attach(value);
        }
    }

    /// <summary>Every paragraph in this container, in order.</summary>
    public IEnumerable<Paragraph> Paragraphs => _items.OfType<Paragraph>();

    /// <summary>Every table in this container, in order.</summary>
    public IEnumerable<Table> Tables => _items.OfType<Table>();

    /// <inheritdoc />
    public void Add(Block item) => _items.Add(Attach(item));

    /// <summary>Adds several blocks in order.</summary>
    public void AddRange(IEnumerable<Block> items)
    {
        foreach (Block item in items)
            Add(item);
    }

    /// <inheritdoc />
    public void Insert(int index, Block item) => _items.Insert(index, Attach(item));

    /// <inheritdoc />
    public bool Remove(Block item)
    {
        if (!_items.Remove(item))
            return false;
        Detach(item);
        return true;
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        Detach(_items[index]);
        _items.RemoveAt(index);
    }

    /// <inheritdoc />
    public void Clear()
    {
        foreach (Block item in _items)
            Detach(item);
        _items.Clear();
    }

    /// <inheritdoc />
    public bool Contains(Block item) => _items.Contains(item);

    /// <inheritdoc />
    public int IndexOf(Block item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void CopyTo(Block[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<Block> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private Block Attach(Block item)
    {
        if (item.Parent is { } previous && !ReferenceEquals(previous, _owner))
            previous.Blocks.Remove(item);
        item.Parent = _owner;
        return item;
    }

    private static void Detach(Block item) => item.Parent = null;
}

/// <summary>
/// Anything that holds blocks: a section body, a table cell, a header, a footnote, a comment.
/// </summary>
public abstract class BlockContainer
{
    /// <summary>Creates a container with an empty block list.</summary>
    protected BlockContainer() => Blocks = new BlockCollection(this);

    /// <summary>The blocks in this container, in order.</summary>
    public BlockCollection Blocks { get; }

    /// <summary>The document this container belongs to, or <see langword="null"/> when detached.</summary>
    public abstract WordDocument? Document { get; }

    /// <summary>Every paragraph in this container, in order.</summary>
    public IEnumerable<Paragraph> Paragraphs => Blocks.Paragraphs;

    /// <summary>Every table in this container, in order.</summary>
    public IEnumerable<Table> Tables => Blocks.Tables;

    /// <summary>Appends a paragraph, optionally with text in it.</summary>
    /// <param name="text">Text of the first run, or <see langword="null"/> for an empty paragraph.</param>
    public Paragraph AddParagraph(string? text = null)
    {
        // The paragraph joins the container before it is filled, so that a document recording
        // tracked changes sees the text arrive and marks it as inserted.
        var paragraph = new Paragraph();
        Blocks.Add(paragraph);
        if (!string.IsNullOrEmpty(text))
            paragraph.AppendText(text);

        if (Document?.ActiveTracking is { } tracking)
            Editing.RevisionRecorder.Added(paragraph, tracking);

        return paragraph;
    }

    /// <summary>Appends a paragraph carrying a named style.</summary>
    /// <param name="text">Text of the first run.</param>
    /// <param name="styleId">Identifier of the paragraph style.</param>
    public Paragraph AddParagraph(string text, string styleId)
    {
        Paragraph paragraph = AddParagraph(text);
        paragraph.Format = paragraph.Format with { StyleId = styleId };
        return paragraph;
    }

    /// <summary>Appends a table with the given shape; every cell starts with one empty paragraph.</summary>
    /// <param name="rows">Number of rows.</param>
    /// <param name="columns">Number of columns.</param>
    public Table AddTable(int rows, int columns)
    {
        var table = Table.Create(rows, columns);
        Blocks.Add(table);
        return table;
    }

    /// <summary>The text of every block in this container, separated by newlines.</summary>
    public string GetText()
    {
        var builder = new System.Text.StringBuilder();
        foreach (Block block in Blocks)
        {
            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(block.GetText());
        }

        return builder.ToString();
    }
}
