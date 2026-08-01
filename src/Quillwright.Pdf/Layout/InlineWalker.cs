using System.Text;
using Inkwright.Text;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Reads a paragraph and hands back what actually prints, in order.
/// </summary>
/// <remarks>
/// The paragraph is one character buffer with runs, objects, marks and wrappers laid over it, so
/// the walk is a single pass over offsets with everything else consulted as it goes. What comes out
/// is free of the things a renderer must not draw: field instructions, deleted text, hidden runs.
/// </remarks>
internal sealed class InlineWalker
{
    private readonly PdfExportContext _context;
    private readonly TextMeasurer _measurer;
    private readonly MathLayouter _math;
    private readonly ChartLayouter _charts;
    private readonly StringBuilder _buffer = new();

    private CharacterStyle? _pendingStyle;
    private Hyperlink? _pendingLink;

    internal InlineWalker(PdfExportContext context, TextMeasurer measurer)
    {
        _context = context;
        _measurer = measurer;
        _math = new MathLayouter(measurer);
        _charts = new ChartLayouter(measurer, context.Diagnostics);
    }

    /// <summary>Numbers the note a reference points at, or answers null when notes are not drawn.</summary>
    public Func<NoteReference, NoteMark?>? Notes { get; set; }

    /// <summary>The number of the note whose own body is being laid out, if that is what this is.</summary>
    public string? NoteMark { get; set; }

    /// <summary>Whether the paragraph being walked reads right-to-left.</summary>
    public bool BaseRightToLeft { get; set; }

    /// <summary>Everything the paragraph prints, in order.</summary>
    /// <param name="paragraph">The paragraph to read.</param>
    public List<InlineItem> Walk(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        List<InlineItem> items = [];
        Dictionary<int, InlineObject> objects = Anchored(paragraph);
        List<(int Start, int End, Hyperlink Link)> links = Links(paragraph);
        bool compute = _context.Options.UpdatePageFields;
        List<(int Start, int End, SimpleField Field)> simple = compute ? SimpleFields(paragraph) : [];
        var fields = new FieldTracker(compute);

        _buffer.Clear();
        _pendingStyle = null;
        _pendingLink = null;

        foreach (Run run in paragraph.Runs)
        {
            if (run.Kind is RunKind.Deleted)
                continue;

            RunFormat resolved = _context.Resolver.ResolveRunFormat(paragraph, run.Format);
            CharacterStyle style = _measurer.Style(resolved);

            if (run.Kind is RunKind.FieldInstruction or RunKind.DeletedFieldInstruction)
            {
                fields.Instruction(run.Span);
                continue;
            }

            bool visible = !style.Hidden || _context.Options.IncludeHiddenText;

            for (int i = 0; i < run.Length; i++)
            {
                int offset = run.Start + i;
                Hyperlink? link = LinkAt(links, offset);

                if (SimpleFieldStart(simple, offset) is { } computed && visible)
                {
                    Flush(items);
                    items.Add(PageField(style, computed, link));
                }

                bool suppressed = InSimpleFieldResult(simple, offset);

                if (objects.TryGetValue(offset, out InlineObject? anchored))
                {
                    Flush(items);
                    Anchor(items, anchored, style, resolved, link, fields, visible && !suppressed);
                    continue;
                }

                if (!visible || suppressed || !fields.Prints)
                    continue;

                char c = run.Span[i];
                switch (c)
                {
                    case '\t':
                        Flush(items);
                        items.Add(InlineItem.Control(InlineKind.Tab, style, link));
                        break;

                    case '\n' or '\r' or '\v':
                        Flush(items);
                        items.Add(InlineItem.Control(InlineKind.LineBreak, style, link));
                        break;

                    default:
                        Append(items, c, style, link);
                        break;
                }
            }
        }

        Flush(items);
        return items;
    }

    private void Append(List<InlineItem> items, char c, CharacterStyle style, Hyperlink? link)
    {
        if (_buffer.Length > 0 && (!ReferenceEquals(_pendingStyle, style) || !ReferenceEquals(_pendingLink, link)))
            Flush(items);

        _pendingStyle = style;
        _pendingLink = link;
        _buffer.Append(c);
    }

    private void Flush(List<InlineItem> items)
    {
        if (_buffer.Length > 0 && _pendingStyle is not null)
            Emit(items, _buffer.ToString(), _pendingStyle, _pendingLink);

        _buffer.Clear();
    }

    /// <summary>
    /// Hands text on, cut into runs of one direction when anything in it reads right-to-left.
    /// Arabic runs are shaped here, before anything is measured: joining changes the letters,
    /// and the letters are what the measurer weighs.
    /// </summary>
    private void Emit(List<InlineItem> items, string text, CharacterStyle style, Hyperlink? link)
    {
        if (!BaseRightToLeft && !BidiLayout.HasRightToLeft(text))
        {
            items.Add(InlineItem.OfText(text, style, link));
            return;
        }

        foreach ((string run, bool rightToLeft) in BidiLayout.Split(text, BaseRightToLeft))
        {
            string shaped = rightToLeft && ArabicShaper.NeedsShaping(run) ? ArabicShaper.Shape(run) : run;
            items.Add(InlineItem.OfText(shaped, style, link, rightToLeft));
        }
    }

    /// <summary>Turns an anchored object into what it prints, if anything.</summary>
    private void Anchor(
        List<InlineItem> items,
        InlineObject anchored,
        CharacterStyle style,
        RunFormat format,
        Hyperlink? link,
        FieldTracker fields,
        bool visible)
    {
        switch (Unwrap(anchored))
        {
            case FieldCharacter boundary:
                if (fields.Boundary(boundary.Kind) is { } computed && visible)
                    items.Add(PageField(style, computed, link));
                break;

            case Break { Kind: BreakKind.Page } when visible && fields.Prints:
                items.Add(InlineItem.Control(InlineKind.PageBreak, style, link));
                break;

            case Break { Kind: BreakKind.Column } when visible && fields.Prints:
                items.Add(InlineItem.Control(InlineKind.ColumnBreak, style, link));
                break;

            case Break when visible && fields.Prints:
                items.Add(InlineItem.Control(InlineKind.LineBreak, style, link));
                break;

            case Picture picture when visible && fields.Prints:
                items.Add(new InlineItem
                {
                    Kind = picture.IsInline ? InlineKind.Picture : InlineKind.FloatingPicture,
                    Style = style,
                    Picture = picture,
                    Link = link,
                });
                break;

            case SymbolCharacter symbol when visible && fields.Prints:
                items.Add(Symbol(symbol, style, link));
                break;

            case RenderedPageBreak or CommentReference or NoteSeparator:
                break;

            case NoteReference reference when visible && fields.Prints:
                if (Notes?.Invoke(reference) is { } note)
                    items.Add(new InlineItem { Kind = InlineKind.NoteReference, Style = style, Note = note, Link = link });

                break;

            // The number the note prints at its own head, which is the same one the text shows.
            case NoteNumberMark when visible && fields.Prints && NoteMark is { Length: > 0 } mark:
                items.Add(InlineItem.OfText(mark, style, link));
                break;

            case Shape shape when visible && fields.Prints:
                // A shape whose markup does not state a size cannot be drawn: there is no box.
                if (shape.Width.Points > 0.5 && shape.Height.Points > 0.5)
                {
                    items.Add(new InlineItem
                    {
                        Kind = shape.IsInline ? InlineKind.Shape : InlineKind.FloatingShape,
                        Style = style,
                        Shape = shape,
                        Link = link,
                    });
                }
                else
                {
                    _context.Diagnostics.Add(
                        PdfExportWarningKind.ContentSkipped,
                        "A shape whose markup does not state its size is not drawn.",
                        "shapes");
                }

                break;

            case MathObject equation when visible && fields.Prints:
                items.Add(new InlineItem
                {
                    Kind = InlineKind.Equation,
                    Style = style,
                    Equation = _math.Layout(equation, format),
                    Link = link,
                });
                break;

            case ChartFrame frame when visible && fields.Prints:
                AddChart(items, frame, style, format, link);
                break;

            case RawInline:
                _context.Diagnostics.Add(
                    PdfExportWarningKind.ContentSkipped,
                    "Content the model keeps verbatim — legacy drawings, embedded objects — is not drawn.",
                    "raw");
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Draws a chart into the frame the document gave it, matching the frame to its part by
    /// name. A floating chart keeps its place in the flow rather than being anchored: the float
    /// machinery understands pictures and text boxes, and putting a chart through it is a
    /// separate piece of work.
    /// </summary>
    private void AddChart(List<InlineItem> items, ChartFrame frame, CharacterStyle style, RunFormat format, Hyperlink? link)
    {
        Chart? chart = frame.Location is null
            ? null
            : _context.Source.Charts.FirstOrDefault(one => one.Location == frame.Location);

        if (_charts.Layout(frame, chart, format) is not { IsEmpty: false } drawn)
            return;

        if (!frame.IsInline)
        {
            _context.Diagnostics.Add(
                PdfExportWarningKind.LayoutApproximated,
                "A floating chart is drawn where it is anchored rather than where it floats.",
                "chart");
        }

        items.Add(new InlineItem { Kind = InlineKind.Chart, Style = style, Chart = drawn, Link = link });
    }

    /// <summary>The object a compatibility wrapper selected, or the object itself.</summary>
    private static InlineObject Unwrap(InlineObject value) =>
        value is AlternateContent alternate ? alternate.Content : value;

    private InlineItem Symbol(SymbolCharacter symbol, CharacterStyle style, Hyperlink? link)
    {
        // A symbol names its own font and a code inside it, usually in the private use area where
        // Wingdings and friends keep their glyphs.
        CharacterStyle symbolStyle = string.IsNullOrEmpty(symbol.Font)
            ? style
            : _measurer.Style(SymbolFormat(symbol.Font, style));

        string text = symbol.Character is > 0 and <= 0x10FFFF && (symbol.Character < 0xD800 || symbol.Character > 0xDFFF)
            ? char.ConvertFromUtf32(symbol.Character)
            : string.Empty;

        return InlineItem.OfText(text, symbolStyle, link);
    }

    private static RunFormat SymbolFormat(string family, CharacterStyle style) => RunFormat.Default with
    {
        FontAscii = family,
        FontHighAnsi = family,
        Size = Primitives.Length.FromPoints(style.LineFontSize),
    };

    private static InlineItem PageField(CharacterStyle style, FieldTracker.PageField field, Hyperlink? link) => new()
    {
        Kind = InlineKind.PageField,
        Style = style,
        Field = field.Kind,
        FieldFormat = field.Format,
        FieldFormatStated = field.FormatStated,
        FieldBookmark = field.Bookmark,
        Link = link,
    };

    private static Dictionary<int, InlineObject> Anchored(Paragraph paragraph)
    {
        Dictionary<int, InlineObject> objects = [];
        foreach ((int offset, InlineObject value) in paragraph.Objects)
            objects[offset] = value;

        return objects;
    }

    private static List<(int Start, int End, Hyperlink Link)> Links(Paragraph paragraph)
    {
        List<(int, int, Hyperlink)> links = [];
        foreach ((int start, int length, InlineRange range) in paragraph.Ranges)
        {
            if (range is Hyperlink link)
                links.Add((start, start + length, link));
        }

        return links;
    }

    private static List<(int Start, int End, SimpleField Field)> SimpleFields(Paragraph paragraph)
    {
        List<(int, int, SimpleField)> fields = [];
        foreach ((int start, int length, InlineRange range) in paragraph.Ranges)
        {
            if (range is SimpleField field && FieldTracker.Parse(field.Instruction) is not null)
                fields.Add((start, start + length, field));
        }

        return fields;
    }

    private static Hyperlink? LinkAt(List<(int Start, int End, Hyperlink Link)> links, int offset)
    {
        foreach ((int start, int end, Hyperlink link) in links)
        {
            if (offset >= start && offset < end)
                return link;
        }

        return null;
    }

    private static FieldTracker.PageField? SimpleFieldStart(
        List<(int Start, int End, SimpleField Field)> fields, int offset)
    {
        foreach ((int start, int _, SimpleField field) in fields)
        {
            if (start == offset)
                return FieldTracker.Parse(field.Instruction);
        }

        return null;
    }

    private static bool InSimpleFieldResult(List<(int Start, int End, SimpleField Field)> fields, int offset)
    {
        foreach ((int start, int end, SimpleField _) in fields)
        {
            if (offset >= start && offset < end)
                return true;
        }

        return false;
    }
}
