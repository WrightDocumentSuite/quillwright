using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Which structure element each piece of content belongs to, when the export is tagged.
/// </summary>
/// <remarks>
/// The tree mirrors what the document means rather than how it is drawn: a heading is an
/// <c>H1</c>, a list item is an <c>LI</c> holding an <c>LBody</c>, a cell is a <c>TD</c>. Elements
/// are named by what they belong to, so a paragraph split across two pages asks for the same
/// element twice and stays one paragraph.
/// </remarks>
internal sealed partial class PageComposer
{
    private readonly List<ListFrame> _lists = [];

    private TagRef? _cellTag;

    private TagRef? TagOf(ParagraphBox box)
    {
        if (!_context.Options.Tagged)
            return null;

        TagRef? parent = _cellTag;

        if (box.Format.NumberingId is { } list and > 0)
            parent = ListBody(box, list, Math.Clamp(box.Format.NumberingLevel ?? 0, 0, 8), parent);
        else if (_cellTag is null)
            _lists.Clear();

        return new TagRef(HeadingOf(box.Format) ?? "P", box.Source) { Parent = parent };
    }

    /// <summary>Headers and footers are decoration, not content, so nothing in them is tagged.</summary>
    private static TagRef? FurnitureTag(ParagraphBox box) => null;

    /// <summary>
    /// The element a picture stands as. A reader that cannot see it is shown the description the
    /// author wrote; one that carries none is still a figure, and the accessibility validator will
    /// say so rather than the picture passing unnoticed.
    /// </summary>
    private TagRef? FigureTag(Picture picture, TagRef? parent) =>
        _context.Options.Tagged
            ? new TagRef("Figure", picture) { Parent = parent, AlternateText = picture.Description }
            : null;

    /// <summary>The tag of a heading, or <see langword="null"/> for an ordinary paragraph.</summary>
    /// <remarks>
    /// The logical structure defines <c>H1</c> to <c>H6</c> and no further, so a document outlined
    /// nine deep has its last levels drawn as the sixth rather than as a tag no reader knows.
    /// </remarks>
    private static string? HeadingOf(ParagraphFormat format) =>
        format.OutlineLevel is { } level and >= 0 and <= 8 ? "H" + Math.Min(6, level + 1) : null;

    /// <summary>
    /// The element a list item's text hangs from, opening and closing the lists around it as the
    /// depth changes.
    /// </summary>
    private TagRef ListBody(ParagraphBox box, int numbering, int level, TagRef? outer)
    {
        while (_lists.Count > 0 && (_lists[^1].Level > level || _lists[^1].Numbering != numbering))
            _lists.RemoveAt(_lists.Count - 1);

        if (_lists.Count == 0 || _lists[^1].Level < level)
        {
            TagRef? parent = _lists.Count > 0 ? _lists[^1].LastBody : outer;
            _lists.Add(new ListFrame
            {
                Numbering = numbering,
                Level = level,
                List = new TagRef("L", new object()) { Parent = parent },
            });
        }

        ListFrame frame = _lists[^1];
        var item = new TagRef("LI", box.Source) { Parent = frame.List };
        var body = new TagRef("LBody", box.Source) { Parent = item };
        frame.LastBody = body;
        return body;
    }

    /// <summary>The structure element of a table cell, and the row and table it sits in.</summary>
    private TagRef? CellTag(Table table, TableRow row, CellBox cell)
    {
        if (!_context.Options.Tagged)
            return null;

        var tableTag = new TagRef("Table", table);
        var rowTag = new TagRef("TR", row) { Parent = tableTag };
        string kind = row.Format.IsHeader == true ? "TH" : "TD";
        return new TagRef(kind, cell.Source) { Parent = rowTag };
    }

    /// <summary>One open list, remembered so that a deeper list knows what to hang from.</summary>
    private sealed class ListFrame
    {
        public required int Numbering { get; init; }

        public required int Level { get; init; }

        public required TagRef List { get; init; }

        public TagRef? LastBody { get; set; }
    }
}
