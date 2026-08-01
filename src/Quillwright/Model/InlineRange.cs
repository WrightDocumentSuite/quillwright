namespace Quillwright.Model;

/// <summary>What kind of tracked edit a revision range records.</summary>
public enum RevisionKind : byte
{
    /// <summary>Content added by the author (<c>w:ins</c>).</summary>
    Inserted = 0,

    /// <summary>Content removed by the author (<c>w:del</c>).</summary>
    Deleted,

    /// <summary>The origin of moved content (<c>w:moveFrom</c>).</summary>
    MovedFrom,

    /// <summary>The destination of moved content (<c>w:moveTo</c>).</summary>
    MovedTo,
}

/// <summary>
/// An element that wraps a stretch of a paragraph rather than sitting at a point: a
/// hyperlink, a tracked edit, a content control. Stored as an offset range over the text
/// buffer, so text can be edited across its boundaries and the wrapper follows.
/// </summary>
public abstract class InlineRange;

/// <summary>A hyperlink over a stretch of text (<c>w:hyperlink</c>).</summary>
public sealed class Hyperlink : InlineRange
{
    /// <summary>The target when the link points outside the document.</summary>
    public string? Url { get; set; }

    /// <summary>The bookmark the link jumps to when it points inside the document.</summary>
    public string? Anchor { get; set; }

    /// <summary>Text shown when the pointer rests on the link.</summary>
    public string? Tooltip { get; set; }

    /// <summary>A location inside the target document.</summary>
    public string? TargetFrame { get; set; }

    /// <summary>Whether following the link adds it to the visited list.</summary>
    public bool AddToHistory { get; set; } = true;

    /// <summary>Relationship id of an external target, preserved from a loaded document.</summary>
    public string? RelationshipId { get; internal set; }

    /// <summary>Attributes of <c>w:hyperlink</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }
}

/// <summary>A tracked insertion, deletion or move (<c>w:ins</c>, <c>w:del</c>, <c>w:moveFrom</c>, <c>w:moveTo</c>).</summary>
public sealed class Revision : InlineRange
{
    /// <summary>What the range records.</summary>
    public RevisionKind Kind { get; set; }

    /// <summary>Identifier of the revision.</summary>
    public int Id { get; set; }

    /// <summary>Who made the change.</summary>
    public string? Author { get; set; }

    /// <summary>When the change was made.</summary>
    public DateTimeOffset? Date { get; set; }

    /// <summary>Name pairing a move source with its destination.</summary>
    public string? MoveName { get; set; }

    /// <summary>Attributes this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }
}

/// <summary>
/// A field written as one element with its instruction as an attribute (<c>w:fldSimple</c>,
/// ISO/IEC 29500-1 §17.16.19).
/// </summary>
/// <remarks>
/// It is the same thing as the begin-instruction-separator-result-end sequence, said in one
/// element instead of five, and Word writes whichever it feels like. The range covers the
/// cached result, so <see cref="FieldExtensions.Fields(Paragraph)"/> can hand back both forms
/// as one <see cref="Field"/> and a caller need not care which the file used.
/// </remarks>
public sealed class SimpleField : InlineRange
{
    /// <summary>The instruction, for example <c>PAGE \* MERGEFORMAT</c>.</summary>
    public string Instruction { get; set; } = string.Empty;

    /// <summary>Whether the cached result is out of date.</summary>
    public bool Dirty { get; set; }

    /// <summary>Whether the result is locked against updates.</summary>
    public bool Locked { get; set; }

    /// <summary>Attributes of <c>w:fldSimple</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>The custom field data (<c>w:fldData</c>), kept verbatim.</summary>
    public string? DataXml { get; set; }
}

/// <summary>A structured document tag around a stretch of text (inline <c>w:sdt</c>).</summary>
public sealed class InlineContentControl : InlineRange
{
    /// <summary>The programmatic tag, used to find the control.</summary>
    public string? Tag { get; set; }

    /// <summary>The friendly title shown in the user interface.</summary>
    public string? Alias { get; set; }

    /// <summary>Identifier of the control.</summary>
    public int? Id { get; set; }

    /// <summary>The full <c>w:sdtPr</c> element, kept verbatim so that the control keeps its type and binding.</summary>
    public string? PropertiesXml { get; set; }

    /// <summary>The <c>w:sdtEndPr</c> element, kept verbatim.</summary>
    public string? EndPropertiesXml { get; set; }
}

/// <summary>
/// A wrapper the model does not interpret — a smart tag, a bidirectional override, a simple
/// field — whose opening and closing markup is kept verbatim around content that is still
/// fully modelled.
/// </summary>
public sealed class RawRange : InlineRange
{
    /// <summary>Creates a preserved wrapper.</summary>
    /// <param name="prefix">Markup emitted before the content, including any property children.</param>
    /// <param name="suffix">Markup emitted after the content.</param>
    public RawRange(string prefix, string suffix)
    {
        Prefix = prefix;
        Suffix = suffix;
    }

    /// <summary>Markup emitted before the content.</summary>
    public string Prefix { get; }

    /// <summary>Markup emitted after the content.</summary>
    public string Suffix { get; }
}
