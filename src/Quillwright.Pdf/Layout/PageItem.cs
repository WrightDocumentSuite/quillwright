using Inkwright;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Something to draw on a page, positioned in the top-down space layout works in.
/// </summary>
/// <remarks>
/// Composition and rendering are kept apart on purpose: composition decides what goes where and
/// may throw a page away and start again, while rendering writes operators and can never be undone.
/// A draw list is the seam between them, and it is deliberately small — a filled rectangle, a
/// stroked segment, a line of text, a picture, a link.
/// </remarks>
internal abstract class PageItem
{
    /// <summary>The left edge, in points from the left of the page.</summary>
    public double X { get; set; }

    /// <summary>The top edge, in points down from the top of the page.</summary>
    public double Y { get; set; }

    /// <summary>
    /// The structure element this item belongs to, or <see langword="null"/> when it is an
    /// artifact. Only meaningful when the export is tagged.
    /// </summary>
    public TagRef? Tag { get; set; }
}

/// <summary>A solid rectangle, used for shading and for table cell fills.</summary>
internal sealed class FillItem : PageItem
{
    /// <summary>How wide the rectangle is.</summary>
    public required double Width { get; init; }

    /// <summary>How tall the rectangle is.</summary>
    public required double Height { get; init; }

    /// <summary>The colour to fill it with.</summary>
    public required PdfColor Color { get; init; }
}

/// <summary>A straight stroked segment, used for borders, underlines and rules.</summary>
internal sealed class StrokeItem : PageItem
{
    /// <summary>Where the segment ends horizontally, in points from the left of the page.</summary>
    public required double X2 { get; init; }

    /// <summary>Where the segment ends vertically, in points down from the top of the page.</summary>
    public required double Y2 { get; init; }

    /// <summary>How thick the segment is.</summary>
    public required double Thickness { get; init; }

    /// <summary>The colour to stroke it in.</summary>
    public required PdfColor Color { get; init; }

    /// <summary>The line style, which decides the dash pattern and whether the line is doubled.</summary>
    public BorderStyle Style { get; init; } = BorderStyle.Single;
}

/// <summary>A laid-out line of text placed on the page.</summary>
internal sealed class TextLineItem : PageItem
{
    /// <summary>The line to draw. Its own coordinates are relative to this item's position.</summary>
    public required LineBox Line { get; init; }

    /// <summary>
    /// How the line is turned, in visual clockwise degrees: 0 for ordinary text, 90 for text
    /// read downwards with its glyph tops to the right, 270 for text read upwards. For a turned
    /// line the position is the visual top-left of the strip the line occupies.
    /// </summary>
    public int Rotation { get; init; }

    /// <summary>How long the strip of a turned line is, which is where reading upwards starts.</summary>
    public double Length { get; init; }

    /// <summary>
    /// Whether comment fragments are emitted as annotations while this line is painted. Furniture
    /// and turned text deliberately leave this false because they can be repeated or transformed.
    /// </summary>
    public bool PaintComments { get; init; }
}

/// <summary>A picture placed on the page.</summary>
internal sealed class ImageItem : PageItem
{
    /// <summary>The picture to draw.</summary>
    public required Picture Picture { get; init; }

    /// <summary>How wide to draw it.</summary>
    public required double Width { get; init; }

    /// <summary>How tall to draw it.</summary>
    public required double Height { get; init; }
}

/// <summary>A clickable area, turned into a link annotation rather than into content.</summary>
internal sealed class LinkItem : PageItem
{
    /// <summary>The Word hyperlink whose fragments this clickable area covers.</summary>
    public required Hyperlink Link { get; init; }

    /// <summary>How wide the clickable area is.</summary>
    public required double Width { get; init; }

    /// <summary>How tall it is.</summary>
    public required double Height { get; init; }

    /// <summary>The address the link opens, when it leads out of the document.</summary>
    public string? Url { get; init; }

    /// <summary>The bookmark the link jumps to, when it leads inside the document.</summary>
    public string? Anchor { get; init; }
}

/// <summary>Where in the finished document a bookmark ended up.</summary>
/// <param name="PageIndex">Which page it landed on, counted from zero.</param>
/// <param name="X">Its left edge in PDF coordinates.</param>
/// <param name="Y">Its top edge in PDF coordinates.</param>
internal readonly record struct BookmarkTarget(int PageIndex, double X, double Y);

/// <summary>
/// Where an item belongs in the structure tree. Items sharing a reference share one element, which
/// is how a paragraph split across two pages stays one paragraph.
/// </summary>
/// <param name="Tag">The structure type, such as <c>P</c> or <c>TD</c>.</param>
/// <param name="Owner">The object the element stands for, used to recognise the same element again.</param>
internal sealed record TagRef(string Tag, object Owner)
{
    /// <summary>The element this one hangs from, or <see langword="null"/> for a top-level element.</summary>
    public TagRef? Parent { get; init; }

    /// <summary>Text that replaces the content for a reader that cannot see it.</summary>
    public string? AlternateText { get; init; }

    /// <inheritdoc />
    public bool Equals(TagRef? other) =>
        other is not null && Tag == other.Tag && ReferenceEquals(Owner, other.Owner);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Tag, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Owner));
}
