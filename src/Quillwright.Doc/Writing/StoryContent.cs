using Quillwright.Model;

namespace Quillwright.Doc.Writing;

/// <summary>A paragraph, cell mark or row mark and the properties that apply to it.</summary>
/// <param name="EndPosition">Character position one past the mark that ends it.</param>
/// <param name="StyleIndex">Index into the stylesheet.</param>
/// <param name="Properties">The packed paragraph modifiers, without the style index.</param>
internal readonly record struct ParagraphSpan(int EndPosition, int StyleIndex, byte[] Properties);

/// <summary>A stretch of characters that share one set of character properties.</summary>
/// <param name="StartPosition">Character position of the first character.</param>
/// <param name="EndPosition">Character position one past the last character.</param>
/// <param name="Properties">The packed character modifiers.</param>
internal readonly record struct RunSpanRecord(int StartPosition, int EndPosition, byte[] Properties);

/// <summary>A section and the page setup that applies to it.</summary>
/// <param name="StartPosition">Character position the section begins at.</param>
/// <param name="Properties">The packed section modifiers.</param>
internal readonly record struct SectionSpan(int StartPosition, byte[] Properties);

/// <summary>A field boundary, recorded so the field tables can point at it.</summary>
/// <param name="Position">Character position of the boundary character.</param>
/// <param name="Kind">Which boundary this is.</param>
/// <param name="FieldType">The numeric field type, meaningful on a begin boundary.</param>
internal readonly record struct FieldSpan(int Position, FieldCharKind Kind, byte FieldType);

/// <summary>A bookmark, recorded as the range it covers.</summary>
/// <param name="Name">The bookmark name.</param>
/// <param name="StartPosition">Character position the bookmark opens at.</param>
/// <param name="EndPosition">Character position the bookmark closes at.</param>
internal readonly record struct BookmarkSpan(string Name, int StartPosition, int EndPosition);

/// <summary>The stretch of text a comment applies to.</summary>
/// <param name="Id">Identifier of the comment.</param>
/// <param name="StartPosition">Character position the commented range opens at.</param>
/// <param name="EndPosition">Character position the commented range closes at.</param>
internal readonly record struct CommentRangeSpan(int Id, int StartPosition, int EndPosition);

/// <summary>A footnote, endnote or comment reference and the story that holds its body.</summary>
/// <param name="ReferencePosition">Character position of the reference character.</param>
/// <param name="Id">Identifier of the note or comment the reference points at.</param>
/// <param name="BodyStart">Character position the body begins at.</param>
/// <param name="CustomMark">Whether the note prints a mark of the author's choosing rather than a number.</param>
internal readonly record struct NoteSpan(int ReferencePosition, int Id, int BodyStart, bool CustomMark = false);

/// <summary>One header, footer or note separator story.</summary>
/// <param name="StartPosition">Character position the story begins at.</param>
/// <param name="EndPosition">Character position one past the story's guard mark.</param>
internal readonly record struct HeaderStorySpan(int StartPosition, int EndPosition);

/// <summary>The characters the binary format reserves for something other than text.</summary>
internal static class DocChar
{
    /// <summary>A picture or other special object anchored in the text.</summary>
    public const char Picture = '\u0001';

    /// <summary>An automatically numbered footnote or endnote reference.</summary>
    public const char NoteReference = '\u0002';

    /// <summary>A comment reference.</summary>
    public const char CommentReference = '\u0005';

    /// <summary>A cell mark, and at table depth one also the row mark.</summary>
    public const char CellMark = '\u0007';

    /// <summary>A tab.</summary>
    public const char Tab = '\u0009';

    /// <summary>A line break inside a paragraph.</summary>
    public const char LineBreak = '\u000B';

    /// <summary>A page break, and the mark that ends a section.</summary>
    public const char PageBreak = '\u000C';

    /// <summary>A paragraph mark.</summary>
    public const char ParagraphMark = '\u000D';

    /// <summary>A column break.</summary>
    public const char ColumnBreak = '\u000E';

    /// <summary>The start of a field.</summary>
    public const char FieldBegin = '\u0013';

    /// <summary>The separator between a field's instruction and its result.</summary>
    public const char FieldSeparator = '\u0014';

    /// <summary>The end of a field.</summary>
    public const char FieldEnd = '\u0015';

    /// <summary>A hyphen that does not break the line.</summary>
    public const char NonBreakingHyphen = '\u001E';

    /// <summary>A hyphen shown only when the word breaks.</summary>
    public const char OptionalHyphen = '\u001F';

    /// <summary>Returns <see langword="true"/> for characters that need the special flag on their run.</summary>
    public static bool IsSpecial(char value) =>
        value is Picture or NoteReference or CommentReference or FieldBegin or FieldSeparator or FieldEnd;
}
