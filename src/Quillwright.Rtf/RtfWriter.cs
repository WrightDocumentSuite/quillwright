using System.Globalization;
using System.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Rtf;

/// <summary>Exports a Quillwright document as deterministic Rich Text Format.</summary>
public static class RtfWriter
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

        ExportResources resources = ExportResources.Create(document);
        var builder = new StringBuilder();
        builder.Append("{\\rtf1\\ansi\\ansicpg1252\\uc1\\deff0");
        resources.Write(builder);
        builder.Append('\n');

        for (int sectionIndex = 0; sectionIndex < document.Sections.Count; sectionIndex++)
        {
            if (sectionIndex > 0)
                builder.Append("\\sect\n");

            foreach (Block block in document.Sections[sectionIndex].Blocks)
                WriteBlock(builder, block, diagnostics, resources, annotations);
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
        AnnotationExportContext? annotations)
    {
        if (block is Paragraph paragraph)
        {
            WriteParagraph(builder, paragraph, diagnostics, resources, annotations);
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
        bool insideAnnotation = false)
    {
        builder.Append("\\pard");
        ParagraphFormat paragraphFormat = insideAnnotation && paragraph.Format.StyleId == "CommentText"
            ? paragraph.Format with { StyleId = null }
            : paragraph.Format;
        WriteParagraphFormat(builder, paragraphFormat, diagnostics);
        builder.Append(' ');

        if (annotationReference)
            builder.Append("{\\plain\\chatn }");

        if (paragraph.Marks.Any(static item => item.Mark is not (CommentRangeStart or CommentRangeEnd)) ||
            paragraph.Ranges.Any())
        {
            diagnostics.Add(
                RtfExportWarningKind.FormattingDropped,
                "Inline marks and ranges are currently unwrapped while their text is retained.",
                "inline-ranges");
        }

        using IEnumerator<(int Offset, InlineMark Mark)> marks = paragraph.Marks.GetEnumerator();
        bool hasMark = marks.MoveNext();

        foreach (Run run in paragraph.Runs)
        {
            if (run.Kind is RunKind.FieldInstruction or RunKind.Deleted or RunKind.DeletedFieldInstruction)
                continue;

            builder.Append("{\\plain");
            bool isCommentReferenceRun = annotations is not null &&
                run.Length == 1 &&
                paragraph.ObjectAt(run.Start) is CommentReference;
            if (!isCommentReferenceRun)
                WriteRunFormat(builder, run.Format, diagnostics, resources);
            builder.Append(' ');

            int end = run.Start + run.Length;
            ReadOnlySpan<char> text = paragraph.AsSpan();
            for (int offset = run.Start; offset < end; offset++)
            {
                while (hasMark && marks.Current.Offset <= offset)
                {
                    WriteMark(builder, marks.Current.Mark, diagnostics, annotations);
                    hasMark = marks.MoveNext();
                }

                if (paragraph.ObjectAt(offset) is { } value)
                {
                    WriteObject(builder, value, diagnostics, resources, annotations);
                    continue;
                }

                WriteCharacter(builder, text[offset]);
            }

            builder.Append('}');
        }

        while (hasMark)
        {
            WriteMark(builder, marks.Current.Mark, diagnostics, annotations);
            hasMark = marks.MoveNext();
        }

        builder.Append("\\par\n");
    }

    private static void WriteRunFormat(
        StringBuilder builder,
        RunFormat format,
        RtfExportDiagnostics diagnostics,
        ExportResources resources)
    {
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
            FontAscii = format.FontAscii,
            FontHighAnsi = format.FontHighAnsi,
            FontEastAsia = format.FontEastAsia,
            FontComplexScript = format.FontComplexScript,
            Bold = format.Bold,
            Italic = format.Italic,
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
        RtfExportDiagnostics diagnostics)
    {
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
        foreach (TabStop tab in format.Tabs)
            WriteTabStop(builder, tab, diagnostics);

        ParagraphFormat supported = ParagraphFormat.Default with
        {
            KeepWithNext = format.KeepWithNext,
            KeepLinesTogether = format.KeepLinesTogether,
            PageBreakBefore = format.PageBreakBefore,
            WidowControl = format.WidowControl,
            SuppressLineNumbers = format.SuppressLineNumbers,
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
        AnnotationExportContext? annotations)
    {
        switch (mark)
        {
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
        AnnotationExportContext? annotations)
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
            builder.Append("}\\chatn {\\*\\annotation");
            builder.Append("{\\*\\atnref ")
                .Append(reference.ToString(CultureInfo.InvariantCulture))
                .Append('}');

            DateTimeOffset? timestamp = comment.DateUtc ?? comment.Date;
            if (timestamp is DateTimeOffset date)
            {
                DateTime wallClock = comment.DateUtc is not null ? date.UtcDateTime : date.DateTime;
                if (TryPackDttm(wallClock, out uint packed))
                {
                    builder.Append("{\\*\\atndate ")
                        .Append(packed.ToString(CultureInfo.InvariantCulture))
                        .Append('}');
                }
                else
                {
                    diagnostics.Add(
                        RtfExportWarningKind.ContentSkipped,
                        $"Comment {commentId} has a date outside the RTF DTTM range and it was omitted.",
                        "comment-date");
                }
            }

            WriteParent(builder, comment, diagnostics);
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
                !_references.TryGetValue(parentId, out int parentReference))
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

            // Keep an explicit parent reference for conforming readers when the model order
            // cannot use Word's adjacent-thread -1 convention.
            builder.Append("{\\*\\atnparent ")
                .Append(parentReference.ToString(CultureInfo.InvariantCulture))
                .Append('}');
            diagnostics.Add(
                RtfExportWarningKind.ContentSkipped,
                "A reply was not adjacent to its thread root; an explicit atnparent reference was emitted, which some Word versions flatten.",
                "comment-thread-order");
        }

        private int RootId(Comment comment)
        {
            var seen = new HashSet<int>();
            while (comment.ParentId is int parentId &&
                   seen.Add(comment.Id) &&
                   _comments.TryGetValue(parentId, out Comment? parent))
            {
                comment = parent;
            }

            return comment.Id;
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

        private ExportResources(Dictionary<string, int> fonts, Dictionary<uint, int> colors)
        {
            _fonts = fonts;
            _colors = colors;
        }

        public static ExportResources Create(WordDocument document)
        {
            string defaultFont = PrimaryFont(document.Styles.DefaultRunFormat) ?? "Times New Roman";
            var fontNames = new HashSet<string>(StringComparer.Ordinal) { defaultFont };
            var colors = new HashSet<uint>();

            foreach (Section section in document.Sections)
            {
                foreach (Paragraph paragraph in section.Blocks.OfType<Paragraph>())
                    AddParagraphResources(paragraph, fontNames, colors);
            }

            foreach (Comment comment in document.Comments)
            {
                foreach (Paragraph paragraph in comment.Blocks.OfType<Paragraph>())
                    AddParagraphResources(paragraph, fontNames, colors);
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

            return new ExportResources(fontIndexes, colorIndexes);
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

        private static void AddParagraphResources(
            Paragraph paragraph,
            HashSet<string> fontNames,
            HashSet<uint> colors)
        {
            foreach (Run run in paragraph.Runs)
            {
                if (PrimaryFont(run.Format) is { Length: > 0 } font)
                    fontNames.Add(font);
                AddColor(colors, run.Format.Color);
                AddColor(colors, run.Format.UnderlineColor);
                if (run.Format.Highlight is HighlightColor highlight && HighlightRgb(highlight) is uint rgb)
                    colors.Add(rgb);
            }
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
