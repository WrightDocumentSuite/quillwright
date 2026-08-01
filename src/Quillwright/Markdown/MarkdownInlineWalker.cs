using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Markdown;

internal enum MarkdownInlineKind : byte
{
    Text = 0,
    LineBreak,
    BlockBreak,
    Tab,
    Picture,
    NoteReference,
    Anchor,
}

internal readonly record struct MarkdownInlineStyle(
    bool Bold,
    bool Italic,
    bool Strike,
    bool Underline,
    bool Code,
    VerticalTextAlignment VerticalAlignment);

internal sealed class MarkdownInlineToken
{
    public required MarkdownInlineKind Kind { get; init; }

    public string? Text { get; set; }

    public MarkdownInlineStyle Style { get; init; }

    /// <summary>
    /// The resolved formatting behind the token, for an exporter — HTML — whose target can say
    /// more than the distilled <see cref="Style"/> carries. Markdown ignores it.
    /// </summary>
    public RunFormat? Resolved { get; init; }

    /// <summary>
    /// The tracked change the token sits inside, for an exporter showing changes marked.
    /// Markdown ignores it; HTML turns it into <c>ins</c> and <c>del</c>.
    /// </summary>
    public RevisionKind? Revision { get; init; }

    public Hyperlink? Link { get; init; }

    public Picture? Picture { get; init; }

    public NoteReference? NoteReference { get; init; }
}

/// <summary>
/// Walks the paragraph buffer with its runs, objects, marks, fields and ranges and returns only
/// the semantic inline content selected by the export options.
/// </summary>
internal static class MarkdownInlineWalker
{
    public static List<MarkdownInlineToken> Walk(Paragraph paragraph, IInlineExportContext context)
    {
        var tokens = new List<MarkdownInlineToken>();
        Dictionary<int, InlineObject> objects = Objects(paragraph);
        Dictionary<int, List<InlineMark>> marks = Marks(paragraph);
        (Dictionary<int, List<InlineRange>> starts, Dictionary<int, List<InlineRange>> ends) = RangeEvents(paragraph);
        var active = new List<InlineRange>();
        var fields = new FieldView(context);
        bool suppressNoteSeparator = false;
        ReportRangeWrappers(paragraph, context);

        int runIndex = 0;
        for (int offset = 0; offset <= paragraph.TextLength; offset++)
        {
            if (ends.TryGetValue(offset, out List<InlineRange>? closing))
            {
                foreach (InlineRange range in closing)
                    active.Remove(range);
            }

            if (starts.TryGetValue(offset, out List<InlineRange>? opening))
                active.AddRange(opening);

            bool revisionVisible = RevisionVisible(active, context.RevisionMode);
            if (revisionVisible && fields.Prints && marks.TryGetValue(offset, out List<InlineMark>? anchoredMarks))
                AddMarks(tokens, anchoredMarks, context);

            if (offset == paragraph.TextLength)
                break;

            while (runIndex + 1 < paragraph.Runs.Count && offset >= paragraph.Runs[runIndex].Start + paragraph.Runs[runIndex].Length)
                runIndex++;

            Run? current = paragraph.Runs.Count > 0 ? paragraph.Runs[runIndex] : null;
            RunFormat resolved = current is { } run
                ? context.Resolver.ResolveRunFormat(run)
                : context.Resolver.ResolveMarkFormat(paragraph);
            MarkdownInlineStyle style = context.DistillStyle(resolved);
            Hyperlink? link = ActiveLink(active, context);
            RevisionKind? revision = ActiveRevision(active);

            if (objects.TryGetValue(offset, out InlineObject? value))
            {
                InlineObject unwrapped = Unwrap(value);
                if (unwrapped is FieldCharacter boundary)
                {
                    fields.Boundary(boundary.Kind);
                    continue;
                }

                if (unwrapped is NoteNumberMark)
                    suppressNoteSeparator = true;

                bool visibleObject = revisionVisible && fields.Prints && VisibleRun(current, active, resolved, context);
                if (visibleObject)
                    AddObject(tokens, unwrapped, style, resolved, revision, link, context);
                continue;
            }

            if (!revisionVisible || !fields.Prints || !VisibleRun(current, active, resolved, context))
                continue;

            char c = paragraph.AsSpan()[offset];
            if (suppressNoteSeparator)
            {
                suppressNoteSeparator = false;
                if (c is ' ' or '\t')
                    continue;
            }

            if (c == InlineObject.Placeholder)
            {
                context.Report(
                    MarkdownExportWarningKind.ContentSkipped,
                    "An inline placeholder has no object at its recorded position.",
                    "orphan-inline-placeholder");
                continue;
            }

            switch (c)
            {
                case '\t':
                    Add(tokens, new MarkdownInlineToken { Kind = MarkdownInlineKind.Tab, Style = style, Resolved = resolved, Revision = revision, Link = link });
                    break;
                case '\r' or '\n' or '\v':
                    Add(tokens, new MarkdownInlineToken { Kind = MarkdownInlineKind.LineBreak, Style = style, Resolved = resolved, Revision = revision, Link = link });
                    break;
                default:
                    AddText(tokens, c.ToString(), style, resolved, revision, link);
                    break;
            }
        }

        fields.Finish();
        return tokens;
    }

    private static void ReportRangeWrappers(
        Paragraph paragraph,
        IInlineExportContext context)
    {
        foreach ((_, int length, InlineRange range) in paragraph.Ranges)
        {
            switch (range)
            {
                case SimpleField when length == 0:
                    context.Report(
                        MarkdownExportWarningKind.ContentSkipped,
                        "A simple field without a cached result was skipped.",
                        "field-without-result");
                    break;
                case InlineContentControl:
                    context.Report(
                        MarkdownExportWarningKind.StructureApproximated,
                        "An inline content-control wrapper is omitted while its content is preserved.",
                        "inline-content-control");
                    break;
                case RawRange:
                    context.Report(
                        MarkdownExportWarningKind.StructureApproximated,
                        "An uninterpreted inline wrapper is omitted while its content is preserved.",
                        "raw-range");
                    break;
            }
        }
    }

    private static bool VisibleRun(
        Run? run,
        List<InlineRange> active,
        RunFormat resolved,
        IInlineExportContext context)
    {
        if (resolved.Hidden == true && !context.IncludeHiddenText)
            return false;

        if (run is not { } value)
            return true;

        if (value.Kind is RunKind.FieldInstruction or RunKind.DeletedFieldInstruction)
            return false;

        if (value.Kind != RunKind.Deleted)
            return true;

        if (context.RevisionMode == MarkdownRevisionMode.Accepted)
            return false;

        if (!active.OfType<Revision>().Any(revision => revision.Kind is RevisionKind.Deleted or RevisionKind.MovedFrom))
        {
            context.Report(
                MarkdownExportWarningKind.StructureApproximated,
                "Deleted run text is not covered by a tracked-change range and is treated as original content.",
                "orphan-deleted-run");
        }

        return true;
    }

    private static bool RevisionVisible(List<InlineRange> active, MarkdownRevisionMode mode)
    {
        foreach (Revision revision in active.OfType<Revision>())
        {
            bool hidden = mode switch
            {
                MarkdownRevisionMode.Accepted => revision.Kind is RevisionKind.Deleted or RevisionKind.MovedFrom,
                MarkdownRevisionMode.Original => revision.Kind is RevisionKind.Inserted or RevisionKind.MovedTo,
                _ => false,
            };

            if (hidden)
                return false;
        }

        return true;
    }

    private static Hyperlink? ActiveLink(List<InlineRange> active, IInlineExportContext context)
    {
        Hyperlink? found = null;
        foreach (Hyperlink link in active.OfType<Hyperlink>())
        {
            if (found is not null)
            {
                context.Report(
                    MarkdownExportWarningKind.StructureApproximated,
                    "Overlapping hyperlinks are flattened to the outermost link.",
                    "overlapping-hyperlinks");
                continue;
            }

            found = link;
        }

        return found;
    }

    /// <summary>The tracked change the position sits inside, a departure counting first.</summary>
    private static RevisionKind? ActiveRevision(List<InlineRange> active)
    {
        RevisionKind? found = null;
        foreach (Revision revision in active.OfType<Revision>())
        {
            if (revision.Kind is RevisionKind.Deleted or RevisionKind.MovedFrom)
                return revision.Kind;

            if (revision.Kind is RevisionKind.Inserted or RevisionKind.MovedTo)
                found ??= revision.Kind;
        }

        return found;
    }

    private static void AddObject(
        List<MarkdownInlineToken> tokens,
        InlineObject value,
        MarkdownInlineStyle style,
        RunFormat resolved,
        RevisionKind? revision,
        Hyperlink? link,
        IInlineExportContext context)
    {
        switch (value)
        {
            case Break { Kind: BreakKind.Line }:
                Add(tokens, new MarkdownInlineToken { Kind = MarkdownInlineKind.LineBreak, Style = style, Resolved = resolved, Revision = revision, Link = link });
                break;
            case Break:
                Add(tokens, new MarkdownInlineToken { Kind = MarkdownInlineKind.BlockBreak, Style = style, Resolved = resolved, Revision = revision, Link = link });
                context.Report(
                    MarkdownExportWarningKind.StructureApproximated,
                    "A page or column break is represented as a Markdown block boundary.",
                    "page-or-column-break");
                break;
            case Picture picture when context.IncludePictures:
                Add(tokens, new MarkdownInlineToken
                {
                    Kind = MarkdownInlineKind.Picture,
                    Style = style,
                    Resolved = resolved,
                    Revision = revision,
                    Link = link,
                    Picture = picture,
                });
                if (!picture.IsInline)
                {
                    context.Report(
                        MarkdownExportWarningKind.StructureApproximated,
                        "A floating picture is emitted at its text anchor; wrapping and position are not preserved.",
                        "floating-picture");
                }

                break;
            case NoteReference reference:
                Add(tokens, new MarkdownInlineToken
                {
                    Kind = MarkdownInlineKind.NoteReference,
                    Style = style,
                    Resolved = resolved,
                    Revision = revision,
                    Link = link,
                    NoteReference = reference,
                });
                break;
            case NoteNumberMark or NoteSeparator or RenderedPageBreak or CommentReference:
                break;
            case SymbolCharacter symbol:
                if (symbol.Character is > 0 and <= 0x10FFFF && symbol.Character is not (>= 0xD800 and <= 0xDFFF))
                    AddText(tokens, char.ConvertFromUtf32(symbol.Character), style, resolved, revision, link);
                else
                    context.Report(MarkdownExportWarningKind.ContentSkipped, "An invalid symbol code point was skipped.", "symbol");
                break;
            case MathObject math:
                if (math.GetText() is { Length: > 0 } equation)
                    AddText(tokens, equation, style, resolved, revision, link);
                context.Report(
                    MarkdownExportWarningKind.FormattingDropped,
                    "An equation is represented by its linear text rather than OMML layout.",
                    "math");
                break;
            case Shape shape:
                if (shape.GetText() is { Length: > 0 } shapeText)
                    AddText(tokens, NormalizeLineEndings(shapeText), style, resolved, revision, link);
                context.Report(
                    MarkdownExportWarningKind.StructureApproximated,
                    "Text-box words are kept at the shape anchor; shape geometry is not preserved.",
                    "text-box");
                break;
            case PositionalTab:
                Add(tokens, new MarkdownInlineToken { Kind = MarkdownInlineKind.Tab, Style = style, Resolved = resolved, Revision = revision, Link = link });
                context.Report(
                    MarkdownExportWarningKind.StructureApproximated,
                    "A positional tab is represented by one space.",
                    "tab");
                break;
            case ChartFrame:
                context.Report(
                    MarkdownExportWarningKind.ContentSkipped,
                    "A chart is a drawing with no Markdown equivalent and was skipped.",
                    "chart");
                break;
            case RawInline:
                context.Report(
                    MarkdownExportWarningKind.ContentSkipped,
                    "Raw inline OOXML cannot be copied into Markdown safely and was skipped.",
                    "raw-inline");
                break;
            default:
                if (value.GetText() is { Length: > 0 } text)
                {
                    AddText(tokens, NormalizeLineEndings(text), style, resolved, revision, link);
                    context.Report(
                        MarkdownExportWarningKind.StructureApproximated,
                        "An inline object is represented by its extracted text.",
                        value.GetType().Name);
                }
                else
                {
                    context.Report(
                        MarkdownExportWarningKind.ContentSkipped,
                        "An unsupported inline object was skipped.",
                        value.GetType().Name);
                }

                break;
        }
    }

    private static void AddMarks(
        List<MarkdownInlineToken> tokens,
        IEnumerable<InlineMark> marks,
        IInlineExportContext context)
    {
        foreach (InlineMark mark in marks)
        {
            if (mark is BookmarkStart bookmark && context.Anchors.For(bookmark) is { } id)
            {
                Add(tokens, new MarkdownInlineToken
                {
                    Kind = MarkdownInlineKind.Anchor,
                    Text = id,
                    Style = default,
                });
            }
            else if (mark is RawMark)
            {
                context.Report(
                    MarkdownExportWarningKind.ContentSkipped,
                    "A raw zero-width Word mark was skipped.",
                    "raw-mark");
            }
        }
    }

    /// <summary>Whether the format names a font every reader of code would recognise as one.</summary>
    internal static bool IsMonospace(RunFormat format)
    {
        string family = string.Join(' ', format.FontAscii, format.FontHighAnsi, format.FontEastAsia).ToLowerInvariant();
        return family.Contains("courier", StringComparison.Ordinal) ||
               family.Contains("mono", StringComparison.Ordinal) ||
               family.Contains("consol", StringComparison.Ordinal) ||
               family.Contains("menlo", StringComparison.Ordinal) ||
               family.Contains("monaco", StringComparison.Ordinal);
    }

    private static InlineObject Unwrap(InlineObject value)
    {
        while (value is AlternateContent alternate)
            value = alternate.Content;
        return value;
    }

    private static Dictionary<int, InlineObject> Objects(Paragraph paragraph)
    {
        Dictionary<int, InlineObject> result = [];
        foreach ((int offset, InlineObject value) in paragraph.Objects)
            result[offset] = value;
        return result;
    }

    private static Dictionary<int, List<InlineMark>> Marks(Paragraph paragraph)
    {
        Dictionary<int, List<InlineMark>> result = [];
        foreach ((int offset, InlineMark mark) in paragraph.Marks)
        {
            if (!result.TryGetValue(offset, out List<InlineMark>? at))
                result[offset] = at = [];
            at.Add(mark);
        }

        return result;
    }

    private static (Dictionary<int, List<InlineRange>> Starts, Dictionary<int, List<InlineRange>> Ends)
        RangeEvents(Paragraph paragraph)
    {
        Dictionary<int, List<InlineRange>> starts = [];
        Dictionary<int, List<InlineRange>> ends = [];
        foreach ((int start, int length, InlineRange range) in paragraph.Ranges)
        {
            if (length <= 0)
                continue;
            Add(starts, start, range);
            Add(ends, start + length, range);
        }

        return (starts, ends);

        static void Add(Dictionary<int, List<InlineRange>> map, int offset, InlineRange range)
        {
            if (!map.TryGetValue(offset, out List<InlineRange>? list))
                map[offset] = list = [];
            list.Add(range);
        }
    }

    private static void AddText(
        List<MarkdownInlineToken> tokens,
        string text,
        MarkdownInlineStyle style,
        RunFormat resolved,
        RevisionKind? revision,
        Hyperlink? link)
    {
        if (text.Length == 0)
            return;

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;

            AddTextCore(tokens, text[start..i], style, resolved, revision, link);
            Add(tokens, new MarkdownInlineToken { Kind = MarkdownInlineKind.LineBreak, Style = style, Resolved = resolved, Revision = revision, Link = link });
            start = i + 1;
        }

        AddTextCore(tokens, text[start..], style, resolved, revision, link);
    }

    private static void AddTextCore(
        List<MarkdownInlineToken> tokens,
        string text,
        MarkdownInlineStyle style,
        RunFormat resolved,
        RevisionKind? revision,
        Hyperlink? link)
    {
        if (text.Length == 0)
            return;

        if (tokens.Count > 0 && tokens[^1] is { Kind: MarkdownInlineKind.Text } previous &&
            previous.Style == style && ReferenceEquals(previous.Link, link) && previous.Revision == revision &&
            (ReferenceEquals(previous.Resolved, resolved) || previous.Resolved == resolved))
        {
            previous.Text += text;
            return;
        }

        Add(tokens, new MarkdownInlineToken
        {
            Kind = MarkdownInlineKind.Text,
            Text = text,
            Style = style,
            Resolved = resolved,
            Revision = revision,
            Link = link,
        });
    }

    private static void Add(List<MarkdownInlineToken> tokens, MarkdownInlineToken token) => tokens.Add(token);

    private static string NormalizeLineEndings(string text) => text.ReplaceLineEndings("\n");

    private sealed class FieldView
    {
        private readonly Stack<State> _open = new();
        private readonly IInlineExportContext _context;

        public FieldView(IInlineExportContext context) => _context = context;

        public bool Prints => _open.All(static state => state.InResult);

        public void Boundary(FieldCharKind kind)
        {
            switch (kind)
            {
                case FieldCharKind.Begin:
                    _open.Push(new State());
                    break;
                case FieldCharKind.Separate when _open.Count > 0:
                    _open.Peek().InResult = true;
                    break;
                case FieldCharKind.End when _open.Count > 0:
                    State closed = _open.Pop();
                    if (!closed.InResult)
                    {
                        _context.Report(
                            MarkdownExportWarningKind.ContentSkipped,
                            "A field without a cached result was skipped.",
                            "field-without-result");
                    }

                    break;
            }
        }

        public void Finish()
        {
            if (_open.Count > 0)
            {
                _context.Report(
                    MarkdownExportWarningKind.ContentSkipped,
                    "An unterminated field was skipped.",
                    "unterminated-field");
            }
        }

        private sealed class State
        {
            public bool InResult { get; set; }
        }
    }
}
