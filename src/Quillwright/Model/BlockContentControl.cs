namespace Quillwright.Model;

/// <summary>
/// A structured document tag wrapping whole blocks (block-level <c>w:sdt</c>). Its content
/// lives in <see cref="Content"/> so that a container and a block can be the same thing
/// without the model needing multiple inheritance.
/// </summary>
public sealed class BlockContentControl : Block
{
    /// <summary>Creates an empty content control.</summary>
    public BlockContentControl() => Content = new ContentControlBody(this);

    /// <summary>The programmatic tag, used to find the control.</summary>
    public string? Tag { get; set; }

    /// <summary>The friendly title shown in the user interface.</summary>
    public string? Alias { get; set; }

    /// <summary>Identifier of the control.</summary>
    public int? Id { get; set; }

    /// <summary>The full <c>w:sdtPr</c> element, kept verbatim so the control keeps its type and data binding.</summary>
    public string? PropertiesXml { get; set; }

    /// <summary>The <c>w:sdtEndPr</c> element, kept verbatim.</summary>
    public string? EndPropertiesXml { get; set; }

    /// <summary>The blocks inside the control.</summary>
    public ContentControlBody Content { get; }

    /// <summary>Shorthand for the blocks inside the control.</summary>
    public BlockCollection Blocks => Content.Blocks;

    /// <inheritdoc />
    public override string GetText() => Content.GetText();

    /// <inheritdoc />
    public override Block Clone()
    {
        var clone = new BlockContentControl
        {
            Tag = Tag,
            Alias = Alias,
            Id = Id,
            PropertiesXml = PropertiesXml,
            EndPropertiesXml = EndPropertiesXml,
        };

        foreach (Block block in Blocks)
            clone.Blocks.Add(block.Clone());
        return clone;
    }
}

/// <summary>The blocks inside a <see cref="BlockContentControl"/>.</summary>
public sealed class ContentControlBody : BlockContainer
{
    private readonly BlockContentControl _owner;

    internal ContentControlBody(BlockContentControl owner) => _owner = owner;

    /// <summary>The control this body belongs to.</summary>
    public BlockContentControl Owner => _owner;

    /// <inheritdoc />
    public override WordDocument? Document => _owner.Document;
}
