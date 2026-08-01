using System.Collections;
using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>
/// A stretch of the document with its own page setup, headers and footers.
/// </summary>
/// <remarks>
/// WordprocessingML has no section element: the body is a flat list of blocks, and a section
/// ends at the paragraph whose properties carry a <c>w:sectPr</c>, with the last section's
/// properties sitting at the end of the body. The loader splits that flat list into sections
/// and the writer flattens it back, so the model can present the structure authors think in.
/// </remarks>
public sealed class Section : BlockContainer
{
    /// <summary>Creates a section with default page setup.</summary>
    public Section()
    {
        Headers = new HeaderFooterSlots(this, isFooter: false);
        Footers = new HeaderFooterSlots(this, isFooter: true);
    }

    /// <summary>The document this section belongs to, or <see langword="null"/> when detached.</summary>
    public WordDocument? Owner { get; internal set; }

    /// <inheritdoc />
    public override WordDocument? Document => Owner;

    /// <summary>Page setup of the section (<c>w:sectPr</c>).</summary>
    public SectionProperties Properties { get; set; } = new();

    /// <summary>The header slots of the section.</summary>
    public HeaderFooterSlots Headers { get; }

    /// <summary>The footer slots of the section.</summary>
    public HeaderFooterSlots Footers { get; }

    /// <summary>Returns an independent copy of the section, not attached to any document.</summary>
    public Section Clone()
    {
        var clone = new Section { Properties = Properties.Clone() };
        foreach (Block block in Blocks)
            clone.Blocks.Add(block.Clone());

        // Headers and footers are shared parts: copying the section points at the same
        // content rather than duplicating it, which matches how Word treats a section split.
        foreach ((HeaderFooterKind kind, HeaderFooter content) in Headers.Defined)
            clone.Headers[kind] = content;
        foreach ((HeaderFooterKind kind, HeaderFooter content) in Footers.Defined)
            clone.Footers[kind] = content;
        return clone;
    }
}

/// <summary>The sections of a document, in order.</summary>
public sealed class SectionCollection : IReadOnlyList<Section>
{
    private readonly List<Section> _items = [];
    private readonly WordDocument _document;

    internal SectionCollection(WordDocument document) => _document = document;

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public Section this[int index] => _items[index];

    /// <summary>The last section, which owns the page setup at the end of the body.</summary>
    public Section Last => _items[^1];

    /// <summary>Appends a section, copying the page setup of the previous one.</summary>
    /// <param name="start">Where the new section begins.</param>
    public Section Add(SectionStart start = SectionStart.NextPage)
    {
        var section = new Section
        {
            Properties = _items.Count > 0 ? _items[^1].Properties.Clone() : new SectionProperties(),
        };

        section.Properties.Start = start;
        Add(section);
        return section;
    }

    /// <summary>Appends an existing section.</summary>
    /// <param name="section">The section to append.</param>
    public void Add(Section section)
    {
        ArgumentNullException.ThrowIfNull(section);
        section.Owner = _document;
        _items.Add(section);
    }

    /// <summary>Inserts a section at the given position.</summary>
    /// <param name="index">Zero-based position.</param>
    /// <param name="section">The section to insert.</param>
    public void Insert(int index, Section section)
    {
        ArgumentNullException.ThrowIfNull(section);
        section.Owner = _document;
        _items.Insert(index, section);
    }

    /// <summary>Removes a section. A document always keeps at least one.</summary>
    /// <param name="section">The section to remove.</param>
    public bool Remove(Section section)
    {
        if (_items.Count <= 1 || !_items.Remove(section))
            return false;
        section.Owner = null;
        return true;
    }

    /// <inheritdoc />
    public IEnumerator<Section> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
