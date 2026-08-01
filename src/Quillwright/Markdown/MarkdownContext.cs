using Quillwright.Model;
using Quillwright.Rendering;
using Quillwright.Styles;

namespace Quillwright.Markdown;

/// <summary>State shared by the block and inline writers for one export.</summary>
internal sealed class MarkdownContext : IInlineExportContext
{
    public MarkdownContext(
        WordDocument document,
        MarkdownExportOptions options,
        string mediaDirectoryName,
        MarkdownExportDiagnostics diagnostics)
    {
        Document = document;
        Options = options;
        Diagnostics = diagnostics;
        Media = new MarkdownMedia(mediaDirectoryName, diagnostics);
        Anchors = new MarkdownAnchorRegistry();
        Notes = new MarkdownNoteRegistry(document, diagnostics);
        Lists = new NumberingCounter(document.Numbering);

        RegisterBookmarks(document.Blocks);
    }

    public WordDocument Document { get; }

    public MarkdownExportOptions Options { get; }

    public MarkdownExportDiagnostics Diagnostics { get; }

    public StyleResolver Resolver => Document.Resolver;

    public MarkdownMedia Media { get; }

    public MarkdownAnchorRegistry Anchors { get; }

    public MarkdownNoteRegistry Notes { get; }

    public NumberingCounter Lists { get; }

    public MarkdownRevisionMode RevisionMode => Options.RevisionMode;

    public bool IncludeHiddenText => Options.IncludeHiddenText;

    public bool IncludePictures => Options.IncludePictures;

    public void Report(MarkdownExportWarningKind kind, string message, string subject) =>
        Diagnostics.Add(kind, message, subject);

    /// <summary>
    /// Distils a resolved format down to what Markdown can say, reporting what it cannot:
    /// colour, highlight, size, family — everything a format-rich target would keep.
    /// </summary>
    public MarkdownInlineStyle DistillStyle(RunFormat format)
    {
        ReportDroppedFormatting(format);
        bool underline = format.Underline is { } underlineStyle && underlineStyle != UnderlineStyle.None;
        if (underline && format.Underline is not UnderlineStyle.Single)
        {
            Diagnostics.Add(
                MarkdownExportWarningKind.StructureApproximated,
                "A complex underline is represented as a single underline.",
                "underline-style");
        }

        return new MarkdownInlineStyle(
            format.Bold == true,
            format.Italic == true,
            format.Strike == true || format.DoubleStrike == true,
            underline,
            MarkdownInlineWalker.IsMonospace(format),
            format.VerticalAlignment ?? VerticalTextAlignment.Baseline);
    }

    private void ReportDroppedFormatting(RunFormat format)
    {
        RunFormat baseline = Document.Styles.DefaultRunFormat;
        if (format.Color is { IsAuto: false })
            Drop("Text colour is not represented in Markdown.", "text-color");
        if (format.Highlight is { } highlight && highlight != HighlightColor.None || format.Shading is { IsEmpty: false })
            Drop("Highlighting and character shading are not represented in Markdown.", "text-highlight");
        if (format.Caps == true || format.SmallCaps == true)
            Drop("Caps and small-caps presentation is not represented in Markdown.", "text-case-effect");
        if (format.CharacterSpacing is { Twips: not 0 } || format.Scale is { } scale && scale != 100 ||
            format.EffectXml is not null || format.Border is not null)
        {
            Drop("Character spacing, scale, borders, and effects are not represented in Markdown.", "character-effects");
        }

        if (format.Size != baseline.Size)
            Drop("Font size is not represented in Markdown.", "font-size");
        if (FontSignature(format) != FontSignature(baseline) && !MarkdownInlineWalker.IsMonospace(format))
            Drop("Font family is not represented in Markdown.", "font-family");
        if (format.ChangeXml is not null)
            Drop("Historical formatting revisions cannot be reconstructed from preserved raw XML.", "format-revision");

        void Drop(string message, string subject) =>
            Diagnostics.Add(MarkdownExportWarningKind.FormattingDropped, message, subject);
    }

    private static string FontSignature(RunFormat format) => string.Join(
        "|", format.FontAscii, format.FontHighAnsi, format.FontEastAsia, format.FontComplexScript,
        format.FontAsciiTheme, format.FontHighAnsiTheme, format.FontEastAsiaTheme, format.FontComplexScriptTheme);

    private void RegisterBookmarks(IEnumerable<Block> blocks)
    {
        foreach (Block block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    foreach ((int _, InlineMark mark) in paragraph.Marks)
                    {
                        if (mark is BookmarkStart bookmark)
                            Anchors.Register(bookmark);
                    }

                    break;

                case Table table:
                    foreach (TableRow row in table.Rows)
                    {
                        foreach (TableCell cell in row.Cells)
                            RegisterBookmarks(cell.Blocks);
                    }

                    break;

                case BlockContentControl control:
                    RegisterBookmarks(control.Blocks);
                    break;

                case AlternateContentBlock alternate:
                    RegisterBookmarks(alternate.Blocks);
                    break;
            }
        }
    }
}
