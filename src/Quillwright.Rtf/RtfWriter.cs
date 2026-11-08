using System.Globalization;
using System.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Rtf;

/// <summary>Exports a Quillwright document as deterministic Rich Text Format.</summary>
public static partial class RtfWriter
{
    /// <summary>Builds an RTF file in memory.</summary>
    /// <param name="document">Document to export.</param>
    /// <param name="options">Export settings.</param>
    public static RtfExportResult Save(WordDocument document, RtfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        _ = options ?? RtfExportOptions.Default;

        var diagnostics = new RtfExportDiagnostics();
        var annotations = new AnnotationExportContext(document, diagnostics);
        var notes = new NoteExportContext(document);

        ExportResources resources = ExportResources.Create(document);
        var builder = new StringBuilder();
        builder.Append("{\\rtf1\\ansi\\ansicpg1252\\uc1\\deff0");
        resources.Write(builder);
        WriteStyleSheet(builder, diagnostics, resources);
        WriteNumberingTables(builder, document, diagnostics, resources);
        WriteDocumentInfo(builder, document.Properties);
        builder.Append('\n');

        for (int sectionIndex = 0; sectionIndex < document.Sections.Count; sectionIndex++)
        {
            if (sectionIndex > 0)
                builder.Append("\\sect\\sectd\n");

            Section section = document.Sections[sectionIndex];
            WriteSectionProperties(builder, section.Properties);
            WriteSectionHeadersAndFooters(builder, section, diagnostics, resources, notes);

            foreach (Block block in section.Blocks)
                WriteBlock(builder, block, diagnostics, resources, annotations, notes);
        }

        annotations.ReportUnemitted(diagnostics);
        builder.Append('}');
        return new RtfExportResult(Encoding.ASCII.GetBytes(builder.ToString()), diagnostics);
    }

    /// <summary>Exports a document directly to a file.</summary>
    /// <param name="document">Document to export.</param>
    /// <param name="path">Destination path.</param>
    /// <param name="options">Export settings.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static async ValueTask<RtfExportDiagnostics> SaveAsync(
        WordDocument document,
        string path,
        RtfExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        RtfExportResult result = Save(document, options);
        await result.SaveAsync(path, cancellationToken).ConfigureAwait(false);
        return result.Diagnostics;
    }

    /// <summary>Exports a document directly to a stream and leaves the stream open.</summary>
    /// <param name="document">Document to export.</param>
    /// <param name="stream">Destination stream.</param>
    /// <param name="options">Export settings.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static async ValueTask<RtfExportDiagnostics> SaveAsync(
        WordDocument document,
        Stream stream,
        RtfExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        RtfExportResult result = Save(document, options);
        await result.SaveAsync(stream, cancellationToken).ConfigureAwait(false);
        return result.Diagnostics;
    }

    private static void WriteBlock(
        StringBuilder builder,
        Block block,
        RtfExportDiagnostics diagnostics,
        ExportResources resources,
        AnnotationExportContext? annotations,
        NoteExportContext? notes = null)
    {
        if (block is Paragraph paragraph)
        {
            WriteParagraph(builder, paragraph, diagnostics, resources, annotations, notes: notes);
            return;
        }

        if (block is Table table)
        {
            WriteTable(builder, table, diagnostics, resources, annotations, notes);
            return;
        }

        diagnostics.Add(
            RtfExportWarningKind.UnsupportedBlock,
            $"{block.GetType().Name} is flattened to plain text.",
            block.GetType().Name);

        string[] lines = block.GetText().Split('\n');
        foreach (string line in lines)
        {
            builder.Append("\\pard ");
            WriteText(builder, line);
            builder.Append("\\par\n");
        }
    }

    private static void WriteParagraph(
        StringBuilder builder,
        Paragraph paragraph,
        RtfExportDiagnostics diagnostics,
        ExportResources resources,
        AnnotationExportContext? annotations,
        bool annotationReference = false,
        bool insideAnnotation = false,
        NoteExportContext? notes = null,
        bool inTable = false,
        bool suppressTrailingPar = false,
        bool insideNote = false)
    {
        builder.Append("\\pard");
        if (inTable)
            builder.Append("\\intbl");

        ParagraphFormat paragraphFormat = paragraph.Format;
        if (insideAnnotation && paragraphFormat.StyleId == "CommentText" ||
            insideNote && paragraphFormat.StyleId is "FootnoteText" or "EndnoteText")
        {
            paragraphFormat = paragraphFormat with { StyleId = null };
        }

        WriteParagraphFormat(builder, paragraphFormat, diagnostics, resources);
        builder.Append(' ');

        if (annotationReference)
            builder.Append("{\\plain\\chatn }");

        if (paragraph.Marks.Any(static item => item.Mark is not (CommentRangeStart or CommentRangeEnd or BookmarkStart or BookmarkEnd)) ||
            paragraph.Ranges.Any(static item => item.Range is not Hyperlink))
        {
            diagnostics.Add(
                RtfExportWarningKind.FormattingDropped,
                "Inline marks and ranges are currently unwrapped while their text is retained.",
                "inline-ranges");
        }

        List<(int Start, int End, Hyperlink Link)> hyperlinks =
        [
            .. paragraph.Ranges
                .Where(static item => item.Length > 0 && item.Range is Hyperlink)
                .Select(static item => (item.Start, item.Start + item.Length, (Hyperlink)item.Range)),
        ];

        using IEnumerator<(int Offset, InlineMark Mark)> marks = paragraph.Marks.GetEnumerator();
        bool hasMark = marks.MoveNext();
        int openHyperlinks = 0;

        foreach (Run run in paragraph.Runs)
        {
            if (run.Kind is RunKind.FieldInstruction or RunKind.Deleted or RunKind.DeletedFieldInstruction)
                continue;

            int runEnd = run.Start + run.Length;
            List<(int Start, int End)> segments = SplitRunSegments(run.Start, runEnd, hyperlinks);
            ReadOnlySpan<char> text = paragraph.AsSpan();

            foreach ((int segStart, int segEnd) in segments)
            {
                foreach ((_, int linkEnd, _) in hyperlinks)
                {
                    if (linkEnd == segStart && openHyperlinks > 0)
                    {
                        builder.Append("}}");
                        openHyperlinks--;
                    }
                }

                while (hasMark && marks.Current.Offset <= segStart)
                {
                    WriteMark(builder, marks.Current.Mark, diagnostics, annotations, resources);
                    hasMark = marks.MoveNext();
                }

                foreach ((int linkStart, _, Hyperlink link) in hyperlinks)
                {
                    if (linkStart == segStart)
                    {
                        WriteHyperlinkStart(builder, link);
                        openHyperlinks++;
                    }
                }

                builder.Append("{\\plain");
                InlineObject? singleObject = run.Length == 1 ? paragraph.ObjectAt(run.Start) : null;
                bool isCommentReferenceRun = annotations is not null && singleObject is CommentReference;
                if (!isCommentReferenceRun)
                {
                    RunFormat runFormat = run.Format.StyleId is "FootnoteReference" or "EndnoteReference"
                        ? run.Format with { StyleId = null }
                        : run.Format;
                    WriteRunFormat(builder, runFormat, diagnostics, resources);
                }
                builder.Append(' ');

                for (int offset = segStart; offset < segEnd; offset++)
                {
                    while (hasMark && marks.Current.Offset <= offset)
                    {
                        WriteMark(builder, marks.Current.Mark, diagnostics, annotations, resources);
                        hasMark = marks.MoveNext();
                    }

                    if (paragraph.ObjectAt(offset) is { } value)
                    {
                        WriteObject(builder, value, diagnostics, resources, annotations, notes);
                        continue;
                    }

                    WriteCharacter(builder, text[offset]);
                }

                builder.Append('}');

                foreach ((_, int linkEnd, _) in hyperlinks)
                {
                    if (linkEnd == segEnd && openHyperlinks > 0)
                    {
                        builder.Append("}}");
                        openHyperlinks--;
                    }
                }
            }
        }

        while (openHyperlinks > 0)
        {
            builder.Append("}}");
            openHyperlinks--;
        }

        while (hasMark)
        {
            WriteMark(builder, marks.Current.Mark, diagnostics, annotations, resources);
            hasMark = marks.MoveNext();
        }

        if (!suppressTrailingPar)
            builder.Append("\\par\n");
    }

    private static void WriteRunFormat(
        StringBuilder builder,
        RunFormat format,
        RtfExportDiagnostics diagnostics,
        ExportResources resources)
    {
        if (format.StyleId is { Length: > 0 } styleId &&
            styleId != "DefaultParagraphFont" &&
            resources.TryGetStyleIndex(styleId, out int styleIndex))
        {
            AppendControl(builder, "cs", styleIndex);
        }

        string? font = PrimaryFont(format);
        if (font is not null)
            AppendControl(builder, "f", resources.FontIndex(font));

        if (HasDifferentFontSlots(format))
        {
            diagnostics.Add(
                RtfExportWarningKind.FormattingDropped,
                "RTF font slots are collapsed to one font for this run.",
                "font-slots");
        }

        AppendToggle(builder, "b", format.Bold);
        AppendToggle(builder, "i", format.Italic);
        AppendToggle(builder, "caps", format.Caps);
        AppendToggle(builder, "scaps", format.SmallCaps);
        AppendToggle(builder, "strike", format.Strike);
        AppendToggle(builder, "striked", format.DoubleStrike);
        AppendToggle(builder, "outl", format.Outline);
        AppendToggle(builder, "shad", format.Shadow);
        AppendToggle(builder, "embo", format.Emboss);
        AppendToggle(builder, "impr", format.Imprint);
        AppendToggle(builder, "v", format.Hidden);
        AppendToggle(builder, "webhidden", format.WebHidden);
        AppendToggle(builder, "noproof", format.NoProof);

        if (format.RightToLeft is bool rightToLeft)
            builder.Append(rightToLeft ? "\\rtlch" : "\\ltrch");
        if (format.Size is Length size)
            AppendControl(builder, "fs", size.HalfPoints);
        if (format.Color is WordColor color)
            AppendControl(builder, "cf", resources.ColorIndex(color, diagnostics));
        if (format.Highlight is HighlightColor highlight)
            AppendControl(builder, "highlight", resources.HighlightIndex(highlight));
        if (format.Underline is UnderlineStyle underline)
            WriteUnderline(builder, underline, diagnostics);
        if (format.UnderlineColor is WordColor underlineColor)
            AppendControl(builder, "ulc", resources.ColorIndex(underlineColor, diagnostics));
        if (format.VerticalAlignment is VerticalTextAlignment verticalAlignment)
        {
            builder.Append(verticalAlignment switch
            {
                VerticalTextAlignment.Superscript => "\\super",
                VerticalTextAlignment.Subscript => "\\sub",
                _ => "\\nosupersub",
            });
        }
        if (format.CharacterSpacing is Length spacing)
            AppendControl(builder, "expndtw", spacing.Twips);
        if (format.Scale is int scale)
            AppendControl(builder, "charscalex", scale);
        if (format.Kerning is Length kerning)
            AppendControl(builder, "kerning", kerning.HalfPoints);
        if (format.Position is Length position)
            AppendControl(builder, position.Twips < 0 ? "dn" : "up", Math.Abs(position.HalfPoints));
        if (format.Language is { Length: > 0 } language)
        {
            try
            {
                AppendControl(builder, "lang", CultureInfo.GetCultureInfo(language).LCID);
            }
            catch (CultureNotFoundException)
            {
                diagnostics.Add(
                    RtfExportWarningKind.FormattingDropped,
                    $"Language '{language}' has no Windows LCID and was omitted.",
                    "language");
            }
        }

        RunFormat supported = RunFormat.Default with
        {
            StyleId = format.StyleId,
            FontAscii = format.FontAscii,
            FontHighAnsi = format.FontHighAnsi,
            FontEastAsia = format.FontEastAsia,
            FontComplexScript = format.FontComplexScript,
            Bold = format.Bold,
            BoldComplexScript = format.BoldComplexScript == format.Bold ? format.BoldComplexScript : null,
            Italic = format.Italic,
            ItalicComplexScript = format.ItalicComplexScript == format.Italic ? format.ItalicComplexScript : null,
            Caps = format.Caps,
            SmallCaps = format.SmallCaps,
            Strike = format.Strike,
            DoubleStrike = format.DoubleStrike,
            Outline = format.Outline,
            Shadow = format.Shadow,
            Emboss = format.Emboss,
            Imprint = format.Imprint,
            NoProof = format.NoProof,
            Hidden = format.Hidden,
            WebHidden = format.WebHidden,
            Color = format.Color,
            CharacterSpacing = format.CharacterSpacing,
            Scale = format.Scale,
            Kerning = format.Kerning,
            Position = format.Position,
            Size = format.Size,
            SizeComplexScript = format.SizeComplexScript == format.Size ? format.SizeComplexScript : null,
            Highlight = format.Highlight,
            Underline = format.Underline,
            UnderlineColor = format.UnderlineColor,
            VerticalAlignment = format.VerticalAlignment,
            RightToLeft = format.RightToLeft,
            Language = format.Language,
        };
        if (format != supported)
        {
            diagnostics.Add(
                RtfExportWarningKind.FormattingDropped,
                "Some run formatting properties have no mapping in the current RTF exporter.",
                "run-format");
        }
    }

    private static void WriteParagraphFormat(
        StringBuilder builder,
        ParagraphFormat format,
        RtfExportDiagnostics diagnostics,
        ExportResources resources)
    {
        if (format.StyleId is { Length: > 0 } styleId &&
            styleId != "Normal" &&
            resources.TryGetStyleIndex(styleId, out int styleIndex))
        {
            AppendControl(builder, "s", styleIndex);
        }

        if (format.Alignment is ParagraphAlignment alignment)
        {
            builder.Append(alignment switch
            {
                ParagraphAlignment.Center => "\\qc",
                ParagraphAlignment.Right => "\\qr",
                ParagraphAlignment.Justify => "\\qj",
                ParagraphAlignment.Distribute => "\\qd",
                _ => "\\ql",
            });
        }

        if (format.IndentLeft is Length left)
            AppendControl(builder, "li", left.Twips);
        if (format.IndentRight is Length right)
            AppendControl(builder, "ri", right.Twips);
        if (format.IndentHanging is Length hanging)
            AppendControl(builder, "fi", -Math.Abs(hanging.Twips));
        else if (format.IndentFirstLine is Length firstLine)
            AppendControl(builder, "fi", firstLine.Twips);
        if (format.SpacingBefore is Length before)
            AppendControl(builder, "sb", before.Twips);
        if (format.SpacingAfter is Length after)
            AppendControl(builder, "sa", after.Twips);
        if (format.LineSpacing is Length lineSpacing)
        {
            int value = format.LineSpacingRule == LineSpacingRule.Exact
                ? -Math.Abs(lineSpacing.Twips)
                : Math.Abs(lineSpacing.Twips);
            AppendControl(builder, "sl", value);
            AppendControl(builder, "slmult", format.LineSpacingRule == LineSpacingRule.Auto ? 1 : 0);
        }

        AppendToggle(builder, "keep", format.KeepLinesTogether);
        AppendToggle(builder, "keepn", format.KeepWithNext);
        AppendToggle(builder, "pagebb", format.PageBreakBefore);
        if (format.WidowControl is bool widowControl)
            builder.Append(widowControl ? "\\widctlpar" : "\\nowidctlpar");
        AppendToggle(builder, "noline", format.SuppressLineNumbers);
        if (format.SuppressAutoHyphens is bool suppressAutoHyphens)
            AppendControl(builder, "hyphpar", suppressAutoHyphens ? 0 : 1);
        AppendToggle(builder, "contextualspace", format.ContextualSpacing);
        if (format.RightToLeft is bool rightToLeft)
            builder.Append(rightToLeft ? "\\rtlpar" : "\\ltrpar");
        if (format.OutlineLevel is int outlineLevel)
            AppendControl(builder, "outlinelevel", Math.Clamp(outlineLevel, 0, 8));
        if (format.NumberingId is int numberingId)
        {
            AppendControl(builder, "ls", numberingId);
            AppendControl(builder, "ilvl", Math.Clamp(format.NumberingLevel ?? 0, 0, 8));
        }
        foreach (TabStop tab in format.Tabs)
            WriteTabStop(builder, tab, diagnostics);
        WriteParagraphBorders(builder, format.Borders, diagnostics, resources);
        if (format.Shading?.Fill is WordColor fill && fill.Kind != ColorKind.Auto)
            AppendControl(builder, "cbpat", resources.ColorIndex(fill, diagnostics));

        ParagraphFormat supported = ParagraphFormat.Default with
        {
            StyleId = format.StyleId,
            KeepWithNext = format.KeepWithNext,
            KeepLinesTogether = format.KeepLinesTogether,
            PageBreakBefore = format.PageBreakBefore,
            WidowControl = format.WidowControl,
            NumberingId = format.NumberingId,
            NumberingLevel = format.NumberingLevel,
            SuppressLineNumbers = format.SuppressLineNumbers,
            Borders = format.Borders,
            Shading = format.Shading,
            SuppressAutoHyphens = format.SuppressAutoHyphens,
            RightToLeft = format.RightToLeft,
            SpacingBefore = format.SpacingBefore,
            SpacingAfter = format.SpacingAfter,
            LineSpacing = format.LineSpacing,
            LineSpacingRule = format.LineSpacingRule,
            IndentLeft = format.IndentLeft,
            IndentRight = format.IndentRight,
            IndentFirstLine = format.IndentFirstLine,
            IndentHanging = format.IndentHanging,
            ContextualSpacing = format.ContextualSpacing,
            Alignment = format.Alignment,
            OutlineLevel = format.OutlineLevel,
            Tabs = format.Tabs,
        };
        if (format != supported)
        {
            diagnostics.Add(
                RtfExportWarningKind.FormattingDropped,
                "Some paragraph formatting properties have no mapping in the current RTF exporter.",
                "paragraph-format");
        }
    }

    private static void WriteTabStop(
        StringBuilder builder,
        TabStop tab,
        RtfExportDiagnostics diagnostics)
    {
        switch (tab.Alignment)
        {
            case TabAlignment.Center:
                builder.Append("\\tqc");
                break;
            case TabAlignment.Right:
                builder.Append("\\tqr");
                break;
            case TabAlignment.Decimal:
                builder.Append("\\tqdec");
                break;
            case TabAlignment.Bar:
                break;
            case TabAlignment.Clear:
            case TabAlignment.Number:
                diagnostics.Add(
                    RtfExportWarningKind.FormattingDropped,
                    $"Tab alignment {tab.Alignment} was approximated as left-aligned.",
                    "tab-alignment");
                break;
        }

        builder.Append(tab.Leader switch
        {
            TabLeader.Dot => "\\tldot",
            TabLeader.Hyphen => "\\tlhyph",
            TabLeader.Underscore => "\\tlul",
            TabLeader.Heavy => "\\tlth",
            TabLeader.MiddleDot => "\\tlmdot",
            _ => string.Empty,
        });
        AppendControl(builder, tab.Alignment == TabAlignment.Bar ? "tb" : "tx", tab.Position.Twips);
    }

    private static void WriteUnderline(
        StringBuilder builder,
        UnderlineStyle underline,
        RtfExportDiagnostics diagnostics)
    {
        string control = underline switch
        {
            UnderlineStyle.None => "\\ulnone",
            UnderlineStyle.Single => "\\ul",
            UnderlineStyle.Words => "\\ulw",
            UnderlineStyle.Double => "\\uldb",
            UnderlineStyle.Thick => "\\ulth",
            UnderlineStyle.Dotted => "\\uld",
            UnderlineStyle.Dash => "\\uldash",
            UnderlineStyle.DotDash => "\\uldashd",
            UnderlineStyle.DotDotDash => "\\uldashdd",
            UnderlineStyle.Wave => "\\ulwave",
            _ => "\\ul",
        };
        builder.Append(control);
        if (control == "\\ul" && underline != UnderlineStyle.Single)
        {
            diagnostics.Add(
                RtfExportWarningKind.FormattingDropped,
                $"Underline style {underline} was approximated as a single underline.",
                "underline-style");
        }
    }

    private static void AppendToggle(StringBuilder builder, string control, bool? value)
    {
        if (value is bool enabled)
            AppendControl(builder, control, enabled ? null : 0);
    }

    private static void AppendControl(StringBuilder builder, string control, int? parameter = null)
    {
        builder.Append('\\').Append(control);
        if (parameter is int value)
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static string? PrimaryFont(RunFormat format) =>
        format.FontAscii ?? format.FontHighAnsi ?? format.FontEastAsia ?? format.FontComplexScript;

    private static bool HasDifferentFontSlots(RunFormat format)
    {
        string? primary = PrimaryFont(format);
        if (primary is null)
            return false;

        return (format.FontAscii is not null && format.FontAscii != primary) ||
               (format.FontHighAnsi is not null && format.FontHighAnsi != primary) ||
               (format.FontEastAsia is not null && format.FontEastAsia != primary) ||
               (format.FontComplexScript is not null && format.FontComplexScript != primary);
    }

    private static void WriteMark(
        StringBuilder builder,
        InlineMark mark,
        RtfExportDiagnostics diagnostics,
        AnnotationExportContext? annotations,
        ExportResources resources)
    {
        switch (mark)
        {
            case BookmarkStart bkmkStart when !string.IsNullOrWhiteSpace(bkmkStart.Name):
                builder.Append("{\\*\\bkmkstart ");
                WriteText(builder, bkmkStart.Name);
                builder.Append('}');
                return;
            case BookmarkEnd bkmkEnd:
                if (resources.TryGetBookmarkName(bkmkEnd.Id, out string? bkmkName) && !string.IsNullOrWhiteSpace(bkmkName))
                {
                    builder.Append("{\\*\\bkmkend ");
                    WriteText(builder, bkmkName);
                    builder.Append('}');
                }
                return;
            case CommentRangeStart start when annotations is not null:
                annotations.WriteRangeMark(builder, start.Id, isStart: true, diagnostics);
                return;
            case CommentRangeEnd end when annotations is not null:
                annotations.WriteRangeMark(builder, end.Id, isStart: false, diagnostics);
                return;
            case CommentRangeStart or CommentRangeEnd:
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    "A nested comment anchor in annotation text was omitted.",
                    "nested-comment-anchor");
                return;
        }
    }

    private static void WriteObject(
        StringBuilder builder,
        InlineObject value,
        RtfExportDiagnostics diagnostics,
        ExportResources resources,
        AnnotationExportContext? annotations,
        NoteExportContext? notes = null)
    {
        if (value is Break lineBreak)
        {
            builder.Append(lineBreak.Kind switch
            {
                BreakKind.Page => "\\page ",
                BreakKind.Column => "\\column ",
                _ => "\\line ",
            });
            return;
        }

        if (value is RenderedPageBreak)
            return;

        if (value is CommentReference commentReference && annotations is not null)
        {
            annotations.WriteAnnotation(builder, commentReference.Id, diagnostics, resources);
            return;
        }

        if (TryWriteFeatureObject(builder, value, diagnostics, resources, notes))
            return;

        diagnostics.Add(
            RtfExportWarningKind.UnsupportedInline,
            $"{value.GetType().Name} is not emitted yet.",
            value.GetType().Name);

        if (value.GetText() is { Length: > 0 } fallback)
            WriteText(builder, fallback);
    }

    private static void WriteCommentBody(
        StringBuilder builder,
        Comment comment,
        RtfExportDiagnostics diagnostics,
        ExportResources resources)
    {
        bool firstParagraph = true;
        foreach (Block block in comment.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                WriteParagraph(
                    builder,
                    paragraph,
                    diagnostics,
                    resources,
                    annotations: null,
                    annotationReference: firstParagraph,
                    insideAnnotation: true);
                firstParagraph = false;
                continue;
            }

            diagnostics.Add(
                RtfExportWarningKind.UnsupportedBlock,
                $"{block.GetType().Name} in annotation text is flattened to plain text.",
                $"comment-{block.GetType().Name}");
            builder.Append("\\pard ");
            if (firstParagraph)
                builder.Append("{\\plain\\chatn }");
            WriteText(builder, block.GetText());
            builder.Append("\\par\n");
            firstParagraph = false;
        }

        if (firstParagraph)
            builder.Append("\\pard {\\plain\\chatn }\\par\n");
    }

    private static void WriteText(StringBuilder builder, ReadOnlySpan<char> text)
    {
        foreach (char value in text)
            WriteCharacter(builder, value);
    }

    private static void WriteCharacter(StringBuilder builder, char value)
    {
        switch (value)
        {
            case '\t':
                builder.Append("\\tab ");
                return;
            case '\r':
                return;
            case '\n':
                builder.Append("\\line ");
                return;
            case '\\':
            case '{':
            case '}':
                builder.Append('\\').Append(value);
                return;
        }

        if (value is >= ' ' and <= '~')
        {
            builder.Append(value);
            return;
        }

        builder.Append("\\u")
            .Append(unchecked((short)value).ToString(CultureInfo.InvariantCulture))
            .Append('?');
    }

    private sealed class AnnotationExportContext
    {
        private readonly Dictionary<int, Comment> _comments = [];
        private readonly Dictionary<int, int> _references = [];
        private readonly Dictionary<string, int> _annotationIdCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _rootIds = [];
        private readonly HashSet<int> _emitted = [];
        private int? _lastThreadRoot;

        public AnnotationExportContext(WordDocument document, RtfExportDiagnostics diagnostics)
        {
            int reference = 1;
            foreach (Comment comment in document.Comments)
            {
                if (!_comments.TryAdd(comment.Id, comment))
                {
                    diagnostics.Add(
                        RtfExportWarningKind.ContentSkipped,
                        $"More than one comment has identifier {comment.Id}; only the first can be addressed by an RTF anchor.",
                        "duplicate-comment-id");
                    continue;
                }

                _references.Add(comment.Id, reference++);
                string? annotationId = NormalizeAnnotationId(comment.Initials);
                if (annotationId is not null)
                    _annotationIdCounts[annotationId] = _annotationIdCounts.GetValueOrDefault(annotationId) + 1;
            }

            if (document.Comments.Any(static comment => comment.IsResolved))
            {
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    "RTF 1.9.1 annotations have no resolved-state field; IsResolved was not exported.",
                    "comment-resolved-state");
            }

            if (document.Comments.Any(static comment => !string.IsNullOrWhiteSpace(comment.ExtensibleExtLstXml)))
            {
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    "Word comment reactions in extLst have no RTF 1.9.1 representation and were not exported.",
                    "comment-reactions");
            }

            if (document.Comments.Any(static comment => comment.IsFollowUp && comment.ParentId is null))
            {
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    "RTF 1.9.1 annotations have no intelligent-placeholder field; the follow-up prompt body was omitted.",
                    "comment-follow-up");
            }
        }

        public void WriteRangeMark(
            StringBuilder builder,
            int commentId,
            bool isStart,
            RtfExportDiagnostics diagnostics)
        {
            if (!_references.TryGetValue(commentId, out int reference))
            {
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    $"A comment range refers to missing comment {commentId} and was omitted.",
                    "orphan-comment-range");
                return;
            }

            builder.Append("{\\*\\")
                .Append(isStart ? "atrfstart " : "atrfend ")
                .Append(reference.ToString(CultureInfo.InvariantCulture))
                .Append('}');
        }

        public void WriteAnnotation(
            StringBuilder builder,
            int commentId,
            RtfExportDiagnostics diagnostics,
            ExportResources resources)
        {
            if (!_comments.TryGetValue(commentId, out Comment? comment))
            {
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    $"A comment reference points to missing comment {commentId} and was omitted.",
                    "orphan-comment-reference");
                return;
            }

            if (!_emitted.Add(commentId))
            {
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    $"Comment {commentId} has more than one reference; its annotation body was emitted only once.",
                    "duplicate-comment-reference");
                return;
            }

            int reference = _references[commentId];
            builder.Append("{\\*\\atnid ");
            WriteText(builder, comment.Initials ?? string.Empty);
            builder.Append("}{\\*\\atnauthor ");
            WriteText(builder, comment.Author ?? string.Empty);
            builder.Append('}');

            DateTimeOffset? timestamp = comment.DateUtc ?? comment.Date;
            DateTime? wallClock = null;
            if (timestamp is DateTimeOffset date)
            {
                wallClock = comment.DateUtc is not null ? date.UtcDateTime : date.DateTime;
                WriteAnnotationTime(builder, wallClock.Value);
            }

            builder.Append("\\chatn {\\*\\annotation");
            if (wallClock is DateTime value)
            {
                if (TryPackDttm(value, out uint packed))
                {
                    builder.Append("{\\*\\atndate ")
                        .Append(packed.ToString(CultureInfo.InvariantCulture))
                        .Append('}');
                }
                else
                {
                    diagnostics.Add(
                        RtfExportWarningKind.ContentSkipped,
                        $"Comment {commentId} has a date outside the packed RTF DTTM range; atntime was written but atndate was omitted.",
                        "comment-date");
                }
            }

            builder.Append("{\\*\\atnref ")
                .Append(reference.ToString(CultureInfo.InvariantCulture))
                .Append('}');
            WriteParent(builder, comment, diagnostics);
            if (comment.IsFollowUp && comment.ParentId is null)
                builder.Append("\\pard\\plain {\\chatn }\\par\n");
            else
                WriteCommentBody(builder, comment, diagnostics, resources);
            builder.Append('}');
        }

        public void ReportUnemitted(RtfExportDiagnostics diagnostics)
        {
            if (_comments.Keys.Any(id => !_emitted.Contains(id)))
            {
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    "A comment without a body reference could not be emitted as an RTF annotation.",
                    "comment-without-reference");
            }
        }

        private void WriteParent(
            StringBuilder builder,
            Comment comment,
            RtfExportDiagnostics diagnostics)
        {
            if (comment.ParentId is not int parentId)
            {
                _lastThreadRoot = comment.Id;
                return;
            }

            if (!_comments.TryGetValue(parentId, out Comment? parent) ||
                !_references.ContainsKey(parentId))
            {
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    $"Comment {comment.Id} refers to missing parent {parentId}; it was exported as a top-level annotation.",
                    "comment-parent");
                _lastThreadRoot = comment.Id;
                return;
            }

            int rootId = RootId(parent);
            if (_lastThreadRoot == rootId)
            {
                // Word writes -1 for every reply following the current top-level annotation.
                // It treats all such replies as one flat thread, which is the interoperable
                // subset of the RTF 1.9.1 atnparent grammar.
                builder.Append("{\\*\\atnparent -1}");
                if (parent.ParentId is not null)
                {
                    diagnostics.Add(
                        RtfExportWarningKind.ContentSkipped,
                        "RTF annotations support a flat reply list; a nested reply was attached to the thread root.",
                        "comment-thread-depth");
                }
                return;
            }

            string? parentAnnotationId = NormalizeAnnotationId(parent.Initials);
            if (parentAnnotationId is null || HasAmbiguousAnnotationId(comment, parent, parentAnnotationId))
            {
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    parentAnnotationId is null
                        ? "A non-adjacent reply has no parent annotation id; its RTF parent link was omitted."
                        : "A non-adjacent reply's parent annotation id is shared by another comment; its ambiguous RTF parent link was omitted.",
                    "comment-parent-annotation-id");
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    "A reply was not adjacent to its thread root and no unambiguous atnparent value could be emitted; it was exported at top level.",
                    "comment-thread-order");
                return;
            }

            // RTF 1.9.1 requires the parent's annotation id here, not the numeric atnref
            // that links an annotation body to its range bookmark.
            builder.Append("{\\*\\atnparent ");
            WriteText(builder, parent.Initials!);
            builder.Append('}');
            diagnostics.Add(
                RtfExportWarningKind.ContentSkipped,
                "A reply was not adjacent to its thread root; an explicit atnparent reference was emitted, which some Word versions flatten.",
                "comment-thread-order");
        }

        private bool HasAmbiguousAnnotationId(Comment comment, Comment parent, string parentAnnotationId)
        {
            int excluded = 1; // The intended parent is not an ambiguity.
            if (comment.Id != parent.Id && NormalizeAnnotationId(comment.Initials) == parentAnnotationId)
                excluded++;
            return _annotationIdCounts.GetValueOrDefault(parentAnnotationId) > excluded;
        }

        private static string? NormalizeAnnotationId(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static void WriteAnnotationTime(StringBuilder builder, DateTime value)
        {
            builder.Append("{\\*\\atntime\\yr")
                .Append(value.Year.ToString(CultureInfo.InvariantCulture))
                .Append("\\mo")
                .Append(value.Month.ToString(CultureInfo.InvariantCulture))
                .Append("\\dy")
                .Append(value.Day.ToString(CultureInfo.InvariantCulture))
                .Append("\\hr")
                .Append(value.Hour.ToString(CultureInfo.InvariantCulture))
                .Append("\\min")
                .Append(value.Minute.ToString(CultureInfo.InvariantCulture))
                .Append("\\sec")
                .Append(value.Second.ToString(CultureInfo.InvariantCulture))
                .Append('}');
        }

        private int RootId(Comment comment)
        {
            if (_rootIds.TryGetValue(comment.Id, out int cachedRootId))
                return cachedRootId;

            var path = new List<int>();
            var seen = new HashSet<int>();
            while (true)
            {
                if (_rootIds.TryGetValue(comment.Id, out cachedRootId))
                {
                    foreach (int commentId in path)
                        _rootIds[commentId] = cachedRootId;
                    return cachedRootId;
                }

                if (!seen.Add(comment.Id))
                    return comment.Id;

                path.Add(comment.Id);
                if (comment.ParentId is int parentId &&
                    _comments.TryGetValue(parentId, out Comment? parent))
                {
                    comment = parent;
                    continue;
                }

                int rootId = comment.Id;
                foreach (int commentId in path)
                    _rootIds[commentId] = rootId;
                return rootId;
            }
        }

        private static bool TryPackDttm(DateTime value, out uint packed)
        {
            int encodedYear = value.Year - 1900;
            if (encodedYear is < 0 or > 511)
            {
                packed = 0;
                return false;
            }

            packed = (uint)value.Minute |
                ((uint)value.Hour << 6) |
                ((uint)value.Day << 11) |
                ((uint)value.Month << 16) |
                ((uint)encodedYear << 20) |
                ((uint)value.DayOfWeek << 29);
            return true;
        }
    }

    private sealed class ExportResources
    {
        private readonly Dictionary<string, int> _fonts;
        private readonly Dictionary<uint, int> _colors;
        private readonly Dictionary<int, string> _bookmarkNames;
        private readonly Dictionary<string, int> _styleIndices;
        private readonly List<(Style Style, int Index)> _orderedStyles;

        private ExportResources(
            Dictionary<string, int> fonts,
            Dictionary<uint, int> colors,
            Dictionary<int, string> bookmarkNames,
            Dictionary<string, int> styleIndices,
            List<(Style Style, int Index)> orderedStyles,
            bool hasStylesToExport)
        {
            _fonts = fonts;
            _colors = colors;
            _bookmarkNames = bookmarkNames;
            _styleIndices = styleIndices;
            _orderedStyles = orderedStyles;
            HasStylesToExport = hasStylesToExport;
        }

        public bool HasStylesToExport { get; }

        public IReadOnlyList<(Style Style, int Index)> OrderedStyles => _orderedStyles;

        public bool TryGetBookmarkName(int id, out string? name) => _bookmarkNames.TryGetValue(id, out name);

        public bool TryGetStyleIndex(string styleId, out int index) => _styleIndices.TryGetValue(styleId, out index);

        public static ExportResources Create(WordDocument document)
        {
            string defaultFont = PrimaryFont(document.Styles.DefaultRunFormat) ?? "Times New Roman";
            var fontNames = new HashSet<string>(StringComparer.Ordinal) { defaultFont };
            var colors = new HashSet<uint>();
            var bookmarkNames = new Dictionary<int, string>();
            var referencedStyleIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (BlockContainer container in document.AllContainers)
            {
                foreach (Block block in container.Blocks)
                    AddBlockResources(block, fontNames, colors, bookmarkNames, referencedStyleIds);
            }

            foreach (AbstractNumbering abstractDef in document.Numbering.Definitions)
            {
                foreach (NumberingLevel level in abstractDef.Levels)
                {
                    if (PrimaryFont(level.RunFormat) is { Length: > 0 } font)
                        fontNames.Add(font);
                    AddColor(colors, level.RunFormat.Color);
                    AddColor(colors, level.RunFormat.UnderlineColor);
                    AddColor(colors, level.ParagraphFormat.Shading?.Fill);
                    AddBorderSetColors(colors, level.ParagraphFormat.Borders);
                }
            }

            bool hasCustomOrModifiedStyles = document.Styles.All.Any(static s =>
                s.IsCustom ||
                (s.Id is not ("Normal" or "DefaultParagraphFont" or "TableNormal" or "NoList" or
                    "FootnoteText" or "EndnoteText" or "FootnoteReference" or "EndnoteReference" or
                    "CommentText" or "CommentReference" or "CommentSubject")) ||
                (s.Id is "Normal" or "DefaultParagraphFont" or "TableNormal" or "NoList" &&
                    (!s.ParagraphFormat.IsEmpty || !s.RunFormat.IsEmpty)));
            bool hasStylesToExport = hasCustomOrModifiedStyles ||
                referencedStyleIds.Any(static id => id is not ("Normal" or "DefaultParagraphFont" or "FootnoteText" or "EndnoteText" or "FootnoteReference" or "EndnoteReference" or "CommentText" or "CommentReference"));

            var styleIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            var orderedStyles = new List<(Style Style, int Index)>();
            if (hasStylesToExport)
            {
                int nextStyleIndex = 0;
                foreach (Style style in document.Styles.All.Where(static s => s.Kind is StyleKind.Paragraph or StyleKind.Character or StyleKind.Table))
                {
                    int idx = style.Id == "Normal" ? 0 : ++nextStyleIndex;
                    if (styleIndices.TryAdd(style.Id, idx))
                    {
                        orderedStyles.Add((style, idx));
                        AddRunFormatResources(style.RunFormat, fontNames, colors);
                        AddColor(colors, style.ParagraphFormat.Shading?.Fill);
                        AddBorderSetColors(colors, style.ParagraphFormat.Borders);
                    }
                }

                foreach (string refId in referencedStyleIds)
                {
                    if (styleIndices.ContainsKey(refId))
                        continue;

                    Style style = document.Styles.GetOrAdd(refId);
                    int idx = ++nextStyleIndex;
                    styleIndices.Add(style.Id, idx);
                    orderedStyles.Add((style, idx));
                    AddRunFormatResources(style.RunFormat, fontNames, colors);
                    AddColor(colors, style.ParagraphFormat.Shading?.Fill);
                    AddBorderSetColors(colors, style.ParagraphFormat.Borders);
                }
            }

            string[] orderedFonts =
            [
                defaultFont,
                .. fontNames.Where(font => font != defaultFont).Order(StringComparer.Ordinal),
            ];
            var fontIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < orderedFonts.Length; index++)
                fontIndexes.Add(orderedFonts[index], index);

            uint[] orderedColors = [.. colors.Order()];
            var colorIndexes = new Dictionary<uint, int>();
            for (int index = 0; index < orderedColors.Length; index++)
                colorIndexes.Add(orderedColors[index], index + 1);

            return new ExportResources(fontIndexes, colorIndexes, bookmarkNames, styleIndices, orderedStyles, hasStylesToExport);
        }

        public int FontIndex(string font) => _fonts[font];

        public int ColorIndex(WordColor color, RtfExportDiagnostics diagnostics)
        {
            if (color.Kind == ColorKind.Rgb && _colors.TryGetValue(color.Rgb, out int index))
                return index;
            if (color.Kind == ColorKind.Theme)
            {
                diagnostics.Add(
                    RtfExportWarningKind.FormattingDropped,
                    "A theme color was emitted as automatic because this RTF export has no theme resolver.",
                    "theme-color");
            }

            return 0;
        }

        public int HighlightIndex(HighlightColor highlight) =>
            HighlightRgb(highlight) is uint rgb && _colors.TryGetValue(rgb, out int index) ? index : 0;

        public void Write(StringBuilder builder)
        {
            builder.Append("{\\fonttbl");
            foreach ((string font, int index) in _fonts.OrderBy(static item => item.Value))
            {
                builder.Append("{\\f").Append(index.ToString(CultureInfo.InvariantCulture)).Append("\\fnil\\fcharset0 ");
                foreach (char value in font)
                    WriteCharacter(builder, value == ';' ? ',' : value);
                builder.Append(";}");
            }
            builder.Append('}');

            if (_colors.Count == 0)
                return;

            builder.Append("{\\colortbl;");
            foreach ((uint rgb, _) in _colors.OrderBy(static item => item.Value))
            {
                builder.Append("\\red").Append(((byte)(rgb >> 16)).ToString(CultureInfo.InvariantCulture));
                builder.Append("\\green").Append(((byte)(rgb >> 8)).ToString(CultureInfo.InvariantCulture));
                builder.Append("\\blue").Append(((byte)rgb).ToString(CultureInfo.InvariantCulture)).Append(';');
            }
            builder.Append('}');
        }

        private static void AddColor(HashSet<uint> colors, WordColor? color)
        {
            if (color is WordColor { Kind: ColorKind.Rgb } rgb)
                colors.Add(rgb.Rgb);
        }

        private static void AddBlockResources(
            Block block,
            HashSet<string> fontNames,
            HashSet<uint> colors,
            Dictionary<int, string> bookmarkNames,
            HashSet<string> referencedStyleIds)
        {
            if (block is Paragraph paragraph)
            {
                AddParagraphResources(paragraph, fontNames, colors, bookmarkNames, referencedStyleIds);
                return;
            }

            if (block is Table table)
            {
                if (table.Format.StyleId is { Length: > 0 } tableStyleId)
                    referencedStyleIds.Add(tableStyleId);
                AddBorderSetColors(colors, table.Format.Borders);

                foreach (TableRow row in table.Rows)
                {
                    foreach (TableCell cell in row.Cells)
                    {
                        AddColor(colors, cell.Format.Shading?.Fill);
                        AddBorderSetColors(colors, cell.Format.Borders);
                        foreach (Block child in cell.Blocks)
                            AddBlockResources(child, fontNames, colors, bookmarkNames, referencedStyleIds);
                    }
                }
            }
        }

        private static void AddBorderSetColors(HashSet<uint> colors, BorderSet? borders)
        {
            if (borders is null)
                return;

            AddColor(colors, borders.Top?.Color);
            AddColor(colors, borders.Left?.Color);
            AddColor(colors, borders.Bottom?.Color);
            AddColor(colors, borders.Right?.Color);
            AddColor(colors, borders.InsideHorizontal?.Color);
            AddColor(colors, borders.InsideVertical?.Color);
            AddColor(colors, borders.Bar?.Color);
        }

        private static void AddParagraphResources(
            Paragraph paragraph,
            HashSet<string> fontNames,
            HashSet<uint> colors,
            Dictionary<int, string> bookmarkNames,
            HashSet<string> referencedStyleIds)
        {
            if (paragraph.Format.StyleId is { Length: > 0 } paraStyleId)
                referencedStyleIds.Add(paraStyleId);
            AddColor(colors, paragraph.Format.Shading?.Fill);
            AddBorderSetColors(colors, paragraph.Format.Borders);

            foreach ((_, InlineMark mark) in paragraph.Marks)
            {
                if (mark is BookmarkStart bkmkStart && !string.IsNullOrWhiteSpace(bkmkStart.Name))
                    bookmarkNames.TryAdd(bkmkStart.Id, bkmkStart.Name);
            }

            foreach (Run run in paragraph.Runs)
            {
                if (run.Format.StyleId is { Length: > 0 } runStyleId)
                    referencedStyleIds.Add(runStyleId);
                AddRunFormatResources(run.Format, fontNames, colors);
            }
        }

        private static void AddRunFormatResources(
            RunFormat format,
            HashSet<string> fontNames,
            HashSet<uint> colors)
        {
            if (PrimaryFont(format) is { Length: > 0 } font)
                fontNames.Add(font);
            AddColor(colors, format.Color);
            AddColor(colors, format.UnderlineColor);
            if (format.Highlight is HighlightColor highlight && HighlightRgb(highlight) is uint rgb)
                colors.Add(rgb);
        }

        private static uint? HighlightRgb(HighlightColor highlight) => highlight switch
        {
            HighlightColor.None => null,
            HighlightColor.Black => 0x000000,
            HighlightColor.Blue => 0x0000FF,
            HighlightColor.Cyan => 0x00FFFF,
            HighlightColor.Green => 0x00FF00,
            HighlightColor.Magenta => 0xFF00FF,
            HighlightColor.Red => 0xFF0000,
            HighlightColor.Yellow => 0xFFFF00,
            HighlightColor.White => 0xFFFFFF,
            HighlightColor.DarkBlue => 0x000080,
            HighlightColor.DarkCyan => 0x008080,
            HighlightColor.DarkGreen => 0x008000,
            HighlightColor.DarkMagenta => 0x800080,
            HighlightColor.DarkRed => 0x800000,
            HighlightColor.DarkYellow => 0x808000,
            HighlightColor.DarkGray => 0x808080,
            HighlightColor.LightGray => 0xC0C0C0,
            _ => null,
        };
    }
}
