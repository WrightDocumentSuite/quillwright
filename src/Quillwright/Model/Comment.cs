namespace Quillwright.Model;

/// <summary>
/// A comment anchored to a range of the document. The anchor lives in the paragraphs as a
/// <see cref="CommentRangeStart"/>/<see cref="CommentRangeEnd"/> pair plus a
/// <see cref="CommentReference"/>; this holds the content of the balloon.
/// </summary>
public sealed class Comment : BlockContainer
{
    internal Comment(WordDocument document) => Owner = document;

    /// <summary>Identifier the anchor refers to.</summary>
    public int Id { get; set; }

    /// <summary>Who wrote the comment.</summary>
    public string? Author { get; set; }

    /// <summary>The author's initials, shown in the margin.</summary>
    public string? Initials { get; set; }

    /// <summary>When the comment was written, as the comments part records it.</summary>
    /// <remarks>
    /// The time zone of <c>w:date</c> is whatever the producer felt like writing, which is why
    /// Word 2018 added <see cref="DateUtc"/> beside it.
    /// </remarks>
    public DateTimeOffset? Date { get; set; }

    /// <summary>
    /// When the comment was written, in UTC, from the Word 2018 extension ([MS-DOCX] 2.10.3.1).
    /// <see langword="null"/> for a document that does not carry that part.
    /// </summary>
    public DateTimeOffset? DateUtc { get; set; }

    /// <summary>
    /// Whether the comment is a follow-up rather than a remark — the <c>intelligentPlaceholder</c>
    /// of [MS-DOCX] 2.10.3.1, which marks a comment whose body is a prompt to be ignored.
    /// </summary>
    /// <remarks>A reply may not be a follow-up, so this is ignored on a comment with a parent.</remarks>
    public bool IsFollowUp { get; set; }

    /// <summary>
    /// Identifier of the comment this one replies to, from the Word 2013 threading
    /// extension ([MS-DOCX] 2.1.2). <see langword="null"/> for a top-level comment.
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>Whether the comment has been marked resolved, from the threading extension.</summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// The identifier that survives renumbering, from the Word 2016 extension ([MS-DOCX] 2.8).
    /// </summary>
    /// <remarks>
    /// <see cref="Id"/> is only an index within one save and Word renumbers it freely. This is
    /// what tells two people editing the same document at once that they are looking at the
    /// same comment, and what the extensible part keys its own metadata by. It is minted on
    /// save for a document that carries the part, so a comment added through the API has none
    /// until then.
    /// </remarks>
    public string? DurableId { get; set; }

    /// <summary>The paragraph identifier that threading uses to link replies, kept as read.</summary>
    internal string? ParagraphId { get; set; }

    /// <summary>
    /// The <c>extLst</c> of the comment's <c>commentExtensible</c> entry, kept verbatim. It
    /// carries the reactions of [MS-OREACTXML], which this version does not model.
    /// </summary>
    internal string? ExtensibleExtLstXml { get; set; }

    /// <summary>Attributes of <c>w:comment</c> this version does not model, kept verbatim.</summary>
    internal string? Attributes { get; set; }

    internal WordDocument Owner { get; }

    /// <inheritdoc />
    public override WordDocument? Document => Owner;
}

/// <summary>What role a note plays in the notes part.</summary>
public enum NoteKind : byte
{
    /// <summary>An ordinary note written by the author.</summary>
    Normal = 0,

    /// <summary>The separator line drawn above the note area.</summary>
    Separator,

    /// <summary>The separator drawn above a note continued from the previous page.</summary>
    ContinuationSeparator,

    /// <summary>The notice printed when a note continues onto the next page.</summary>
    ContinuationNotice,
}

/// <summary>
/// A footnote or an endnote. The reference in the text is a
/// <see cref="Model.NoteReference"/> object; this holds the note's own content.
/// </summary>
public sealed class Note : BlockContainer
{
    internal Note(WordDocument document, bool isEndnote)
    {
        Owner = document;
        IsEndnote = isEndnote;
    }

    /// <summary>Identifier the reference points at.</summary>
    public int Id { get; set; }

    /// <summary>Whether this is an endnote rather than a footnote.</summary>
    public bool IsEndnote { get; }

    /// <summary>What role the note plays.</summary>
    public NoteKind Kind { get; set; } = NoteKind.Normal;

    internal WordDocument Owner { get; }

    /// <inheritdoc />
    public override WordDocument? Document => Owner;
}
