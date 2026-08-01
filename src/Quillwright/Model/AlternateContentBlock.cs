namespace Quillwright.Model;

/// <summary>
/// A block-level <c>mc:AlternateContent</c> whose selected branch this version reads
/// (ISO/IEC 29500-3 §9.3): the blocks of that branch are modelled, while the wrapper and the
/// alternatives around it are kept verbatim.
/// </summary>
/// <remarks>
/// <para>
/// Word wraps a whole paragraph in a compatibility block whenever it holds something an older
/// reader would not understand — a chart, a SmartArt diagram, a modern text effect — and writes
/// a plainer version of the same content in the fallback. Preserving the whole thing keeps a
/// round trip perfect and leaves the content invisible: the words are not in
/// <c>GetText</c>, find-and-replace does not reach them, and nothing draws them.
/// </para>
/// <para>
/// Resolving the block fixes that for the branch a reader of this vocabulary would show, and
/// changes nothing about how it is written: the markup either side of the selected branch's
/// content is sliced out of the original bytes and put back around whatever the blocks
/// regenerate, so the alternatives survive unchanged.
/// </para>
/// </remarks>
public sealed class AlternateContentBlock : Block
{
    /// <summary>Creates a resolved compatibility block.</summary>
    /// <param name="prefix">Markup up to the start of the selected branch's content.</param>
    /// <param name="suffix">Markup from the end of that content onwards.</param>
    public AlternateContentBlock(string prefix, string suffix)
    {
        Prefix = prefix;
        Suffix = suffix;
        Content = new AlternateContentBody(this);
    }

    /// <summary>Markup emitted before the content, ending with the selected branch's start tag.</summary>
    public string Prefix { get; }

    /// <summary>Markup emitted after the content, holding the branches that were not selected.</summary>
    public string Suffix { get; }

    /// <summary>The blocks of the selected branch.</summary>
    public AlternateContentBody Content { get; }

    /// <summary>Shorthand for the blocks of the selected branch.</summary>
    public BlockCollection Blocks => Content.Blocks;

    /// <inheritdoc />
    public override string GetText() => Content.GetText();

    /// <inheritdoc />
    public override Block Clone()
    {
        var clone = new AlternateContentBlock(Prefix, Suffix);
        foreach (Block block in Blocks)
            clone.Blocks.Add(block.Clone());

        return clone;
    }
}

/// <summary>The blocks of the branch an <see cref="AlternateContentBlock"/> selected.</summary>
public sealed class AlternateContentBody : BlockContainer
{
    private readonly AlternateContentBlock _owner;

    internal AlternateContentBody(AlternateContentBlock owner) => _owner = owner;

    /// <summary>The compatibility block this body belongs to.</summary>
    public AlternateContentBlock Owner => _owner;

    /// <inheritdoc />
    public override WordDocument? Document => _owner.Document;
}
