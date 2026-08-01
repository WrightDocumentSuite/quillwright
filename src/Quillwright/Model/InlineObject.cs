using Quillwright.Primitives;

namespace Quillwright.Model;

/// <summary>What a break interrupts (<c>w:br/@w:type</c>).</summary>
public enum BreakKind : byte
{
    /// <summary>Ends the line and continues on the next one.</summary>
    Line = 0,

    /// <summary>Ends the page.</summary>
    Page,

    /// <summary>Ends the column.</summary>
    Column,
}

/// <summary>Which floating objects a line break clears (<c>w:br/@w:clear</c>).</summary>
public enum BreakClear : byte
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The leading side.</summary>
    Left,

    /// <summary>The trailing side.</summary>
    Right,

    /// <summary>Both sides.</summary>
    All,
}

/// <summary>The role of a field character (<c>w:fldChar/@w:fldCharType</c>).</summary>
public enum FieldCharKind : byte
{
    /// <summary>Starts a field; the instruction follows.</summary>
    Begin = 0,

    /// <summary>Separates the instruction from the cached result.</summary>
    Separate,

    /// <summary>Ends a field.</summary>
    End,
}

/// <summary>
/// Something that sits at one position inside a paragraph and is not plain text: a break, an
/// image, a note reference, a field boundary. Each occupies exactly one character of the
/// paragraph's text buffer so that every offset in the paragraph stays meaningful.
/// </summary>
public abstract class InlineObject
{
    /// <summary>The character that stands in for an object with no textual equivalent.</summary>
    public const char Placeholder = '\uFFFC';

    /// <summary>The character this object occupies in the text buffer.</summary>
    public virtual char PlaceholderChar => Placeholder;

    /// <summary>
    /// Whether the object is written inside a <c>w:r</c> element. A few — an equation, a
    /// preserved fragment that arrived beside the runs rather than inside one — belong
    /// directly under the paragraph instead.
    /// </summary>
    public virtual bool IsRunChild => true;

    /// <summary>
    /// Words the object carries of its own, or <see langword="null"/> when it carries none.
    /// A text box has them; a page break does not.
    /// </summary>
    public virtual string? GetText() => null;
}

/// <summary>A line, page or column break (<c>w:br</c>).</summary>
public sealed class Break : InlineObject
{
    /// <summary>What the break interrupts.</summary>
    public BreakKind Kind { get; set; }

    /// <summary>Which floating objects the break clears.</summary>
    public BreakClear Clear { get; set; }

    /// <inheritdoc />
    public override char PlaceholderChar => '\n';
}

/// <summary>A character taken from a symbol font (<c>w:sym</c>).</summary>
public sealed class SymbolCharacter : InlineObject
{
    /// <summary>Name of the font the glyph comes from.</summary>
    public string Font { get; set; } = string.Empty;

    /// <summary>Code point of the glyph within the font.</summary>
    public int Character { get; set; }
}

/// <summary>A reference to a footnote or an endnote (<c>w:footnoteReference</c>, <c>w:endnoteReference</c>).</summary>
public sealed class NoteReference : InlineObject
{
    /// <summary>Whether this points at an endnote rather than a footnote.</summary>
    public bool IsEndnote { get; set; }

    /// <summary>Identifier of the note.</summary>
    public int Id { get; set; }

    /// <summary>Whether the mark is supplied by the author instead of being numbered.</summary>
    public bool CustomMark { get; set; }
}

/// <summary>
/// The automatic number printed at the start of a note's own text (<c>w:footnoteRef</c>,
/// <c>w:endnoteRef</c>). It belongs inside the note, not in the body that references it.
/// </summary>
public sealed class NoteNumberMark : InlineObject
{
    /// <summary>Whether this belongs to an endnote rather than a footnote.</summary>
    public bool IsEndnote { get; set; }
}

/// <summary>The anchor a comment is attached to (<c>w:commentReference</c>).</summary>
public sealed class CommentReference : InlineObject
{
    /// <summary>Identifier of the comment.</summary>
    public int Id { get; set; }
}

/// <summary>A field boundary (<c>w:fldChar</c>).</summary>
public sealed class FieldCharacter : InlineObject
{
    /// <summary>Which boundary this is.</summary>
    public FieldCharKind Kind { get; set; }

    /// <summary>Whether the field result is locked against updates.</summary>
    public bool Locked { get; set; }

    /// <summary>Whether the cached result is out of date.</summary>
    public bool Dirty { get; set; }

    /// <summary>The form-field definition carried by a <see cref="FieldCharKind.Begin"/> character, kept verbatim.</summary>
    public string? FormFieldXml { get; set; }
}

/// <summary>A picture placed in the text flow (<c>w:drawing</c> containing a single <c>pic:pic</c>).</summary>
public sealed class Picture : InlineObject
{
    private ImageData _image = null!;
    private Length _width;
    private Length _height;
    private string? _name;
    private string? _description;
    private bool _isInline = true;
    private PictureAnchor? _anchor;

    /// <summary>The image this picture displays.</summary>
    public required ImageData Image
    {
        get => _image;
        set => Set(ref _image, value);
    }

    /// <summary>Rendered width.</summary>
    public Length Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    /// <summary>Rendered height.</summary>
    public Length Height
    {
        get => _height;
        set => Set(ref _height, value);
    }

    /// <summary>Name shown in the selection pane.</summary>
    public string? Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>Alternative text.</summary>
    public string? Description
    {
        get => _description;
        set => Set(ref _description, value);
    }

    /// <summary>Whether the picture flows with the text rather than floating.</summary>
    public bool IsInline
    {
        get => _isInline;
        set => Set(ref _isInline, value);
    }

    /// <summary>
    /// Where a floating picture sits and how the text behaves around it, or
    /// <see langword="null"/> when it flows with the text or nothing said.
    /// </summary>
    public PictureAnchor? Anchor
    {
        get => _anchor;
        set => Set(ref _anchor, value);
    }

    /// <summary>
    /// The original <c>w:drawing</c> markup when the picture came from a file. It is written
    /// back verbatim unless a property changed, which keeps effects, cropping and wrapping
    /// that this version does not model.
    /// </summary>
    /// <remarks>
    /// Changing any modelled property gives that up: the markup is regenerated from what the
    /// model holds, so anything it does not carry — a float and its text wrapping above all —
    /// is not in the result.
    /// </remarks>
    public string? OriginalXml { get; set; }

    /// <summary>Set when a property changed and the markup has to be regenerated.</summary>
    internal bool IsDirty { get; set; }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        IsDirty = true;
    }
}

/// <summary>
/// A run-level <c>mc:AlternateContent</c> whose selected branch this version reads
/// (ISO/IEC 29500-3 §9.3): the branch is modelled in <see cref="Content"/>, while the
/// wrapper and the alternatives around it are kept verbatim.
/// </summary>
/// <remarks>
/// A picture Word wraps in a compatibility block is a picture all the same, so resolving
/// the wrapper is what makes it visible to the media API and resizable. Only the selected
/// branch is regenerated on save; the alternative — usually the VML drawing an older reader
/// falls back to — is written back as the bytes it arrived as.
/// </remarks>
public sealed class AlternateContent : InlineObject
{
    /// <summary>Creates a resolved compatibility block.</summary>
    /// <param name="prefix">Markup up to the start of the selected branch's content.</param>
    /// <param name="content">The modelled content of the selected branch.</param>
    /// <param name="suffix">Markup from the end of the selected branch's content onwards.</param>
    public AlternateContent(string prefix, InlineObject content, string suffix)
    {
        Prefix = prefix;
        Content = content;
        Suffix = suffix;
    }

    /// <summary>Markup emitted before the content, ending with the selected branch's start tag.</summary>
    public string Prefix { get; }

    /// <summary>The content of the selected branch.</summary>
    public InlineObject Content { get; }

    /// <summary>Markup emitted after the content, holding the branches that were not selected.</summary>
    public string Suffix { get; }

    /// <inheritdoc />
    public override char PlaceholderChar => Content.PlaceholderChar;
}

/// <summary>
/// Markup inside a run that the model does not interpret — an equation, a VML picture, an
/// embedded OLE object, an ink annotation — kept verbatim so that saving does not lose it.
/// </summary>
public sealed class RawInline : InlineObject
{
    /// <summary>Creates a preserved fragment.</summary>
    /// <param name="xml">The verbatim markup.</param>
    /// <param name="isRunChild">Whether the fragment belongs inside a <c>w:r</c> element.</param>
    public RawInline(string xml, bool isRunChild = true)
    {
        Xml = xml;
        IsRunChild = isRunChild;
    }

    /// <summary>The verbatim markup.</summary>
    public string Xml { get; }

    /// <inheritdoc />
    public override bool IsRunChild { get; }
}

/// <summary>
/// The page break Word records where it last laid the text out (<c>w:lastRenderedPageBreak</c>).
/// It carries no formatting; it is preserved so that a saved document keeps Word's pagination hints.
/// </summary>
public sealed class RenderedPageBreak : InlineObject;

/// <summary>The separator line of the footnote or endnote area (<c>w:separator</c>, <c>w:continuationSeparator</c>).</summary>
public sealed class NoteSeparator : InlineObject
{
    /// <summary>Whether this is the separator drawn above a continued note.</summary>
    public bool IsContinuation { get; set; }
}

/// <summary>An absolute-position tab (<c>w:ptab</c>), kept verbatim.</summary>
public sealed class PositionalTab : InlineObject
{
    /// <summary>Creates a preserved positional tab.</summary>
    /// <param name="xml">The verbatim markup.</param>
    public PositionalTab(string xml) => Xml = xml;

    /// <summary>The verbatim markup.</summary>
    public string Xml { get; }
}
