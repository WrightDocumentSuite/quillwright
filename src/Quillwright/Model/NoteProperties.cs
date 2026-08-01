using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>Where the notes of a document are printed (<c>w:footnotePr/w:pos</c>).</summary>
public enum NotePosition : byte
{
    /// <summary>At the bottom of the page the reference is on.</summary>
    PageBottom = 0,

    /// <summary>Directly under the last line of text, which on a short page is not the bottom.</summary>
    BeneathText,

    /// <summary>Together at the end of the section.</summary>
    SectionEnd,

    /// <summary>Together at the end of the document.</summary>
    DocumentEnd,
}

/// <summary>When note numbering starts again (<c>w:footnotePr/w:numRestart</c>).</summary>
public enum NoteRestart : byte
{
    /// <summary>Never: the notes of the whole document count as one series.</summary>
    Continuous = 0,

    /// <summary>At each section.</summary>
    EachSection,

    /// <summary>At each page.</summary>
    EachPage,
}

/// <summary>
/// How a document prints and numbers its footnotes or its endnotes.
/// </summary>
/// <remarks>
/// The element these come from is kept verbatim, both in the settings part and in a section that
/// overrides it, so this is a reading of those bytes rather than a second copy of them. That is
/// why there is nothing to set: what a document says about its notes is what it says.
/// </remarks>
public sealed record NoteProperties
{
    /// <summary>What a document means by footnotes when it says nothing.</summary>
    public static NoteProperties FootnoteDefaults { get; } = new();

    /// <summary>What a document means by endnotes when it says nothing.</summary>
    public static NoteProperties EndnoteDefaults { get; } = new()
    {
        Position = NotePosition.DocumentEnd,
        NumberFormat = ListNumberFormat.LowerRoman,
    };

    /// <summary>Where the notes are printed.</summary>
    public NotePosition Position { get; init; } = NotePosition.PageBottom;

    /// <summary>How the numbers are written.</summary>
    public ListNumberFormat NumberFormat { get; init; } = ListNumberFormat.Decimal;

    /// <summary>The number the first note takes.</summary>
    public int Start { get; init; } = 1;

    /// <summary>When the numbering starts again.</summary>
    public NoteRestart Restart { get; init; } = NoteRestart.Continuous;
}
