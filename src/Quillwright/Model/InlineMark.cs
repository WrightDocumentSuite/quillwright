namespace Quillwright.Model;

/// <summary>
/// A zero-width point inside a paragraph: the start or end of a bookmark, of a commented
/// range, of an editing permission. It sits between two characters and takes no space in the
/// text buffer, which is exactly how WordprocessingML models it.
/// </summary>
public abstract class InlineMark;

/// <summary>The opening point of a bookmark (<c>w:bookmarkStart</c>).</summary>
public sealed class BookmarkStart : InlineMark
{
    /// <summary>Identifier pairing this mark with its <see cref="BookmarkEnd"/>.</summary>
    public int Id { get; set; }

    /// <summary>The bookmark name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>First table column the bookmark covers, when it marks a range of cells.</summary>
    public int? ColumnFirst { get; set; }

    /// <summary>Last table column the bookmark covers, when it marks a range of cells.</summary>
    public int? ColumnLast { get; set; }
}

/// <summary>The closing point of a bookmark (<c>w:bookmarkEnd</c>).</summary>
public sealed class BookmarkEnd : InlineMark
{
    /// <summary>Identifier pairing this mark with its <see cref="BookmarkStart"/>.</summary>
    public int Id { get; set; }
}

/// <summary>The opening point of a commented range (<c>w:commentRangeStart</c>).</summary>
public sealed class CommentRangeStart : InlineMark
{
    /// <summary>Identifier of the comment.</summary>
    public int Id { get; set; }
}

/// <summary>The closing point of a commented range (<c>w:commentRangeEnd</c>).</summary>
public sealed class CommentRangeEnd : InlineMark
{
    /// <summary>Identifier of the comment.</summary>
    public int Id { get; set; }
}

/// <summary>
/// A zero-width element the model does not interpret — a proofing error marker, an editing
/// permission boundary, a Word extension — kept verbatim at its position.
/// </summary>
public sealed class RawMark : InlineMark
{
    /// <summary>Creates a preserved point element.</summary>
    /// <param name="xml">The verbatim markup.</param>
    public RawMark(string xml) => Xml = xml;

    /// <summary>The verbatim markup.</summary>
    public string Xml { get; }
}
