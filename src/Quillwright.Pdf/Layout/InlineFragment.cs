using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// One indivisible piece of a line: a stretch of text in one appearance, a tab, or an object such
/// as a picture. Fragments carry their own metrics so a line can be measured without asking the
/// font anything a second time.
/// </summary>
internal abstract class InlineFragment
{
    /// <summary>Where the fragment starts, measured from the left edge of the line's text area.</summary>
    public double X { get; set; }

    /// <summary>How wide the fragment is, in points.</summary>
    public double Width { get; set; }

    /// <summary>How far the fragment reaches above the baseline, in points.</summary>
    public double Ascent { get; set; }

    /// <summary>How far the fragment reaches below the baseline, in points.</summary>
    public double Descent { get; set; }

    /// <summary>
    /// How tall a single-spaced line carrying this fragment would be. A picture does not raise it,
    /// which is why this is separate from <see cref="Ascent"/> and <see cref="Descent"/>.
    /// </summary>
    public double LineHeight { get; set; }

    /// <summary>The link this fragment sits inside, or <see langword="null"/>.</summary>
    public Hyperlink? Link { get; set; }

    /// <summary>
    /// The structure element this fragment belongs to when it is not simply part of the paragraph
    /// around it — a picture is a figure of its own. Only meaningful when the export is tagged.
    /// </summary>
    public TagRef? Tag { get; set; }
}

/// <summary>A stretch of text drawn in one appearance.</summary>
internal sealed class TextFragment : InlineFragment
{
    /// <summary>The characters, in logical order — the order they are read aloud.</summary>
    public required string Text { get; init; }

    /// <summary>How to draw them.</summary>
    public required CharacterStyle Style { get; init; }

    /// <summary>How many spaces the fragment holds, which is what justification stretches.</summary>
    public int SpaceCount { get; init; }

    /// <summary>Whether the fragment is nothing but white space, so a line may end on it.</summary>
    public bool IsWhitespace { get; init; }

    /// <summary>Whether the fragment reads right-to-left.</summary>
    public bool RightToLeft { get; init; }

    /// <summary>
    /// The characters in the order they are drawn, when that differs from the order they are
    /// read: a right-to-left fragment reversed by the bidi pass, brackets mirrored.
    /// </summary>
    public string? Visual { get; set; }

    /// <summary>What the painter shows.</summary>
    public string Shown => Visual ?? Text;

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// The horizontal jump a tab character makes. Its width is not known until the line is being
/// filled, because a tab runs to the next stop rather than covering a fixed distance.
/// </summary>
internal sealed class TabFragment : InlineFragment
{
    /// <summary>How to draw the leader and how tall the tab makes the line.</summary>
    public required CharacterStyle Style { get; init; }

    /// <summary>The filler drawn across the jump.</summary>
    public TabLeader Leader { get; set; }

    /// <summary>How the text after the stop lines up on it.</summary>
    public TabAlignment Alignment { get; set; } = TabAlignment.Left;

    /// <summary>Whether the stop is a bar rather than a jump, in which case a rule is drawn.</summary>
    public bool IsBar { get; set; }
}

/// <summary>
/// A zero-width comment endpoint carried through line breaking so its PDF annotation lands on the
/// page and line where Word put the comment reference.
/// </summary>
internal sealed class CommentFragment : InlineFragment
{
    /// <summary>The comment whose thread starts here.</summary>
    public required Comment Comment { get; init; }

    /// <summary>Whether this marker participates in a right-to-left run.</summary>
    public bool RightToLeft { get; set; }
}

/// <summary>A picture drawn inside the line.</summary>
internal sealed class ImageFragment : InlineFragment
{
    /// <summary>The picture to draw.</summary>
    public required Picture Picture { get; init; }
}

/// <summary>
/// A text box standing in the line. The fragment holds the room the box takes; the box itself —
/// fill, frame and the words inside — is drawn by the composer, which is the one that knows
/// where the line landed.
/// </summary>
internal sealed class ShapeFragment : InlineFragment
{
    /// <summary>The shape to draw.</summary>
    public required Shape Shape { get; init; }
}

/// <summary>An equation standing in the line, already laid out against its own origin.</summary>
internal sealed class EquationFragment : InlineFragment
{
    /// <summary>The glyph runs and lines to draw, and the room they take.</summary>
    public required EquationLayout Layout { get; init; }
}

/// <summary>A chart standing in the line, already drawn into the frame the document reserved.</summary>
internal sealed class ChartFragment : InlineFragment
{
    /// <summary>The shapes, lines and labels to paint.</summary>
    public required ChartLayout Layout { get; init; }
}

/// <summary>
/// A field result whose text is only known once the page it lands on is known: a page number, a
/// page count. The text measured during layout is the best estimate; the renderer prints the truth.
/// </summary>
internal sealed class PageFieldFragment : InlineFragment
{
    /// <summary>Which quantity the field prints.</summary>
    public required PageFieldKind Kind { get; init; }

    /// <summary>How to draw it.</summary>
    public required CharacterStyle Style { get; init; }

    /// <summary>The numbering scheme, taken from the field switch or from the section.</summary>
    public ListNumberFormat Format { get; set; } = ListNumberFormat.Decimal;

    /// <summary>Whether the field's own instruction named that scheme.</summary>
    public bool FormatStated { get; init; }

    /// <summary>The bookmark a <c>PAGEREF</c> points at; nothing for the other kinds.</summary>
    public string? Bookmark { get; init; }

    /// <summary>The text measured at layout time, printed when nothing better is known.</summary>
    public string Estimate { get; set; } = "1";
}

/// <summary>The quantities a page-related field can print.</summary>
internal enum PageFieldKind
{
    /// <summary>The number of the page the field sits on (<c>PAGE</c>).</summary>
    Page,

    /// <summary>The number of pages in the document (<c>NUMPAGES</c>).</summary>
    NumPages,

    /// <summary>The number of pages in the section (<c>SECTIONPAGES</c>).</summary>
    SectionPages,

    /// <summary>The number of the page a bookmark lands on (<c>PAGEREF</c>).</summary>
    PageRef,
}
