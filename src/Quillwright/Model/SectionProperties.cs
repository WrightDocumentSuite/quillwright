using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>The page margins of a section (<c>w:pgMar</c>).</summary>
public sealed class PageMargins
{
    /// <summary>Space above the body text.</summary>
    public Length Top { get; set; } = Length.FromInches(1);

    /// <summary>Space below the body text.</summary>
    public Length Bottom { get; set; } = Length.FromInches(1);

    /// <summary>Space before the body text.</summary>
    public Length Left { get; set; } = Length.FromInches(1);

    /// <summary>Space after the body text.</summary>
    public Length Right { get; set; } = Length.FromInches(1);

    /// <summary>Distance from the top of the page to the header.</summary>
    public Length Header { get; set; } = Length.FromInches(0.5);

    /// <summary>Distance from the bottom of the page to the footer.</summary>
    public Length Footer { get; set; } = Length.FromInches(0.5);

    /// <summary>Extra space reserved for binding.</summary>
    public Length Gutter { get; set; }

    /// <summary>Returns an independent copy.</summary>
    public PageMargins Clone() => (PageMargins)MemberwiseClone();
}

/// <summary>One column of a multi-column section (<c>w:col</c>).</summary>
/// <param name="Width">Column width.</param>
/// <param name="Space">Space after the column.</param>
public readonly record struct TextColumn(Length Width, Length Space);

/// <summary>The column layout of a section (<c>w:cols</c>).</summary>
public sealed class ColumnLayout
{
    /// <summary>Number of columns.</summary>
    public int Count { get; set; } = 1;

    /// <summary>Space between columns when they are equally wide.</summary>
    public Length Space { get; set; } = Length.FromInches(0.5);

    /// <summary>Whether all columns have the same width.</summary>
    public bool EqualWidth { get; set; } = true;

    /// <summary>Whether a vertical line is drawn between columns.</summary>
    public bool Separator { get; set; }

    /// <summary>Individual column widths, used when <see cref="EqualWidth"/> is <see langword="false"/>.</summary>
    public List<TextColumn> Columns { get; } = [];

    /// <summary>Returns an independent copy.</summary>
    public ColumnLayout Clone()
    {
        var clone = new ColumnLayout { Count = Count, Space = Space, EqualWidth = EqualWidth, Separator = Separator };
        clone.Columns.AddRange(Columns);
        return clone;
    }
}

/// <summary>How page numbers are formatted and restarted in a section (<c>w:pgNumType</c>).</summary>
public sealed class PageNumbering
{
    /// <summary>Numbering scheme, or <see langword="null"/> to inherit.</summary>
    public ListNumberFormat? Format { get; set; }

    /// <summary>The name of a scheme this version does not know.</summary>
    public string? CustomFormat { get; set; }

    /// <summary>Page number the section starts at, or <see langword="null"/> to continue.</summary>
    public int? Start { get; set; }

    /// <summary>Chapter heading style level that prefixes the number.</summary>
    public int? ChapterStyleLevel { get; set; }

    /// <summary>Separator between chapter number and page number.</summary>
    public string? ChapterSeparator { get; set; }

    /// <summary>Returns <see langword="true"/> when nothing is specified.</summary>
    public bool IsEmpty => Format is null && Start is null && ChapterStyleLevel is null && ChapterSeparator is null;

    /// <summary>Returns an independent copy.</summary>
    public PageNumbering Clone() => (PageNumbering)MemberwiseClone();
}

/// <summary>
/// The page setup of a section (<c>w:sectPr</c>). Mutable rather than a record: a document
/// has a handful of sections, and <c>section.Properties.Orientation = Landscape</c> reads
/// better than rebuilding an immutable graph for a one-off change.
/// </summary>
public sealed class SectionProperties
{
    /// <summary>Where the section begins (<c>w:type</c>).</summary>
    public SectionStart Start { get; set; } = SectionStart.NextPage;

    /// <summary>Page width (<c>w:pgSz/@w:w</c>). Defaults to A4.</summary>
    public Length PageWidth { get; set; } = Length.FromMillimeters(210);

    /// <summary>Page height (<c>w:pgSz/@w:h</c>). Defaults to A4.</summary>
    public Length PageHeight { get; set; } = Length.FromMillimeters(297);

    /// <summary>Page orientation (<c>w:pgSz/@w:orient</c>).</summary>
    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;

    /// <summary>Printer paper-size code (<c>w:pgSz/@w:code</c>).</summary>
    public int? PaperCode { get; set; }

    /// <summary>Page margins (<c>w:pgMar</c>).</summary>
    public PageMargins Margins { get; set; } = new();

    /// <summary>Column layout (<c>w:cols</c>).</summary>
    public ColumnLayout Columns { get; set; } = new();

    /// <summary>Page numbering (<c>w:pgNumType</c>).</summary>
    public PageNumbering PageNumbering { get; set; } = new();

    /// <summary>Borders drawn around the page (<c>w:pgBorders</c>).</summary>
    public BorderSet? PageBorders { get; set; }

    /// <summary>Attributes of <c>w:pgBorders</c> other than the edges, kept verbatim.</summary>
    public string? PageBordersAttributes { get; set; }

    /// <summary>Vertical alignment of the text on the page (<c>w:vAlign</c>).</summary>
    public VerticalCellAlignment? VerticalAlignment { get; set; }

    /// <summary>Gives the first page of the section its own header and footer (<c>w:titlePg</c>).</summary>
    public bool DifferentFirstPage { get; set; }

    /// <summary>Right-to-left section (<c>w:bidi</c>).</summary>
    public bool RightToLeft { get; set; }

    /// <summary>Places the gutter on the right (<c>w:rtlGutter</c>).</summary>
    public bool RightToLeftGutter { get; set; }

    /// <summary>Flow direction of the section text (<c>w:textDirection</c>).</summary>
    public TextDirection? TextDirection { get; set; }

    /// <summary>Protects the section from editing except in form fields (<c>w:formProt</c>).</summary>
    public bool? FormProtection { get; set; }

    /// <summary>Suppresses endnotes in this section (<c>w:noEndnote</c>).</summary>
    public bool? SuppressEndnotes { get; set; }

    /// <summary>The line-numbering element, kept verbatim (<c>w:lnNumType</c>).</summary>
    public string? LineNumberingXml { get; set; }

    /// <summary>The document-grid element, kept verbatim (<c>w:docGrid</c>).</summary>
    public string? DocumentGridXml { get; set; }

    /// <summary>The footnote-placement element, kept verbatim (<c>w:footnotePr</c>).</summary>
    public string? FootnotePropertiesXml { get; set; }

    /// <summary>The endnote-placement element, kept verbatim (<c>w:endnotePr</c>).</summary>
    public string? EndnotePropertiesXml { get; set; }

    /// <summary>
    /// How this section prints and numbers its footnotes, or <see langword="null"/> when it says
    /// nothing and the document's own settings decide.
    /// </summary>
    public NoteProperties? FootnoteProperties => FootnotePropertiesXml is { Length: > 0 } xml
        ? Formats.NotePropertiesReader.Parse(xml, endnotes: false)
        : null;

    /// <summary>
    /// How this section prints and numbers its endnotes, or <see langword="null"/> when it says
    /// nothing and the document's own settings decide.
    /// </summary>
    public NoteProperties? EndnoteProperties => EndnotePropertiesXml is { Length: > 0 } xml
        ? Formats.NotePropertiesReader.Parse(xml, endnotes: true)
        : null;

    /// <summary>The paper-source element, kept verbatim (<c>w:paperSrc</c>).</summary>
    public string? PaperSourceXml { get; set; }

    /// <summary>The printer-settings reference, kept verbatim (<c>w:printerSettings</c>).</summary>
    public string? PrinterSettingsXml { get; set; }

    /// <summary>The revision record of a section change, kept verbatim (<c>w:sectPrChange</c>).</summary>
    public string? ChangeXml { get; set; }

    /// <summary>Attributes of <c>w:sectPr</c> itself (revision ids), kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>Children of <c>w:sectPr</c> this version does not model.</summary>
    public string? Extensions { get; set; }

    /// <summary>
    /// Header and footer references as they were read, before the loader can resolve them to
    /// parts. Cleared once the section is wired up.
    /// </summary>
    internal List<Formats.SectionReader.Reference>? LoadedReferences { get; set; }

    /// <summary>The width available to body text, after margins and gutter.</summary>
    public Length ContentWidth => PageWidth - Margins.Left - Margins.Right - Margins.Gutter;

    /// <summary>Sets the page size from a standard paper size, keeping the orientation.</summary>
    public SectionProperties SetPaperSize(Length width, Length height)
    {
        (PageWidth, PageHeight) = Orientation == PageOrientation.Landscape ? (height, width) : (width, height);
        return this;
    }

    /// <summary>Returns an independent copy.</summary>
    public SectionProperties Clone()
    {
        var clone = (SectionProperties)MemberwiseClone();
        clone.Margins = Margins.Clone();
        clone.Columns = Columns.Clone();
        clone.PageNumbering = PageNumbering.Clone();
        return clone;
    }
}
