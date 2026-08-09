using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Measures a paragraph: reads its content, breaks it into lines, gives each line a height and
/// lines the lines up between the indents.
/// </summary>
/// <remarks>
/// Nothing here knows about pages. A paragraph is measured once, in full, against the width of the
/// container it sits in; the composer then decides how much of it fits where. That separation is
/// what makes a paragraph that spans three pages cost one measurement rather than three.
/// </remarks>
internal sealed class ParagraphLayouter
{
    /// <summary>The line spacing value that means single, in the 240ths <c>w:line</c> counts in.</summary>
    private const double SingleSpacing = 240;

    /// <summary>What Word puts above and below a paragraph whose spacing is left to the consumer.</summary>
    private const double AutoSpacing = 14;

    private readonly PdfExportContext _context;
    private readonly TextMeasurer _measurer;
    private readonly InlineWalker _walker;

    internal ParagraphLayouter(PdfExportContext context, TextMeasurer measurer)
    {
        _context = context;
        _measurer = measurer;
        _walker = new InlineWalker(context, measurer);
    }

    /// <summary>How a page field should be measured before pagination has settled.</summary>
    public required Func<PageFieldKind, ListNumberFormat, string?, string> EstimateField { get; init; }

    /// <summary>Content to put at the start of the first line, such as a list marker.</summary>
    public Func<Paragraph, ParagraphFormat, IReadOnlyList<InlineItem>?>? Prefix { get; init; }

    /// <summary>Numbers the note a reference points at.</summary>
    public Func<NoteReference, NoteMark?>? Notes { get; init; }

    /// <summary>
    /// The number of the note whose own body is being laid out. A note opens with a mark that
    /// prints the same number the text showed, and only the caller knows which note this is.
    /// </summary>
    public string? NoteMark { get; set; }

    /// <summary>Measures a paragraph against the width of its container.</summary>
    /// <param name="paragraph">The paragraph to measure.</param>
    /// <param name="containerWidth">How wide the container is, in points.</param>
    public ParagraphBox Layout(Paragraph paragraph, double containerWidth) =>
        Layout(paragraph, containerWidth, shape: null, replay: null);

    /// <summary>
    /// Measures a paragraph, keeping its lines out of the shape's obstacles. Measuring moves the
    /// list and note counters, so measuring the same paragraph a second time — once the composer
    /// knows a float overlaps it — replays what the first measurement resolved instead.
    /// </summary>
    /// <param name="paragraph">The paragraph to measure.</param>
    /// <param name="containerWidth">How wide the container is, in points.</param>
    /// <param name="shape">The room the floats leave, or <see langword="null"/> for the plain width.</param>
    /// <param name="replay">The first measurement, whose marker and note marks are reused.</param>
    public ParagraphBox Layout(Paragraph paragraph, double containerWidth, FlowShape? shape, ParagraphBox? replay)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        ParagraphFormat format = _context.Resolver.ResolveParagraphFormat(paragraph);
        CharacterStyle mark = _measurer.MarkStyle(paragraph);
        ParagraphMetrics metrics = MetricsOf(format, containerWidth);

        List<NoteMark?> marks = Record(replay);
        _walker.NoteMark = NoteMark;
        _walker.BaseRightToLeft = format.RightToLeft == true;

        var breaker = new LineBreaker(
            metrics,
            mark,
            shape,
            line => { Measure(line, format); return line.Height; },
            EmptyHeight(format, mark),
            format.SuppressAutoHyphens == true ? null : _context.Hyphenation)
        {
            EstimateField = EstimateField,
        };

        IReadOnlyList<InlineItem>? prefix = replay is null ? Prefix?.Invoke(paragraph, format) : replay.PrefixItems;
        if (prefix is not null)
        {
            foreach (InlineItem item in prefix)
                breaker.Add(item);
        }

        List<Picture> floats = [];
        List<Shape> floatingShapes = [];
        foreach (InlineItem item in _walker.Walk(paragraph))
        {
            if (item.Kind == InlineKind.FloatingPicture)
            {
                if (item.Picture is { } picture)
                    floats.Add(picture);

                continue;
            }

            if (item.Kind == InlineKind.FloatingShape)
            {
                if (item.Shape is { } floating)
                    floatingShapes.Add(floating);

                continue;
            }

            breaker.Add(item);
        }

        List<LineBox> lines = breaker.Finish();

        foreach (LineBox line in lines)
        {
            Measure(line, format);
            BidiLine.Reorder(line, format.RightToLeft == true);
            Align(line, format);
        }

        var box = new ParagraphBox
        {
            Source = paragraph,
            Format = format,
            Lines = lines,
            SpacingBefore = SpaceBefore(format, lines[0]),
            SpacingAfter = SpaceAfter(format, lines[^1]),
            IndentLeft = metrics.IndentLeft,
            IndentRight = metrics.IndentRight,
            ContainerWidth = containerWidth,
            PageBreakBefore = format.PageBreakBefore == true,
            KeepWithNext = format.KeepWithNext == true,
            KeepLinesTogether = format.KeepLinesTogether == true,
            WidowControl = format.WidowControl != false,
            ContextualSpacing = format.ContextualSpacing == true,
            Shading = format.Shading,
            Borders = format.Borders,
        };

        box.Floats.AddRange(floats);
        box.FloatingShapes.AddRange(floatingShapes);
        box.PrefixItems = prefix;
        box.NoteMarks = marks;
        return box;
    }

    /// <summary>
    /// Points the walker at the note resolver: recording what it answers on a first measurement,
    /// replaying the recording on a second, so the counters move exactly once per paragraph.
    /// </summary>
    private List<NoteMark?> Record(ParagraphBox? replay)
    {
        if (replay is not null)
        {
            List<NoteMark?> recorded = replay.NoteMarks;
            int at = 0;
            _walker.Notes = _ => at < recorded.Count ? recorded[at++] : null;
            return recorded;
        }

        List<NoteMark?> marks = [];
        if (Notes is { } resolve)
            _walker.Notes = reference =>
            {
                NoteMark? mark = resolve(reference);
                marks.Add(mark);
                return mark;
            };
        else
            _walker.Notes = null;

        return marks;
    }

    /// <summary>How tall a line of nothing would be, which is what a band is first fitted with.</summary>
    private static double EmptyHeight(ParagraphFormat format, CharacterStyle mark)
    {
        var probe = new LineBox { EmptyStyle = mark };
        Measure(probe, format);
        return probe.Height;
    }

    private ParagraphMetrics MetricsOf(ParagraphFormat format, double containerWidth)
    {
        double first = format.IndentFirstLine?.Points ?? 0;
        if (format.IndentHanging is { } hanging)
            first = -hanging.Points;

        double indentLeft = format.IndentLeft?.Points ?? 0;

        return new ParagraphMetrics
        {
            ContainerWidth = containerWidth,
            IndentLeft = indentLeft,
            IndentRight = format.IndentRight?.Points ?? 0,
            FirstLineIndent = first,
            Alignment = format.Alignment ?? ParagraphAlignment.Left,
            Tabs = Stops(format.Tabs, first < 0 ? indentLeft : null),
            DefaultTabStop = _context.Source.Settings.DefaultTabStop.Points,
        };
    }

    /// <summary>
    /// The stops a paragraph offers, sorted. The outdent of a hanging indent is a stop of its own
    /// even though nothing declares it: without it the tab after a list marker would overshoot to
    /// the default grid and the text would not line up under itself.
    /// </summary>
    private static IReadOnlyList<TabStop> Stops(EquatableArray<TabStop> declared, double? hangingStop)
    {
        if (declared.IsEmpty && hangingStop is null)
            return [];

        List<TabStop> stops = [.. declared];
        if (hangingStop is { } at)
            stops.Add(new TabStop(Length.FromPoints(at)));

        stops.Sort(static (left, right) => left.Position.Twips.CompareTo(right.Position.Twips));
        return stops;
    }

    /// <summary>Gives a line its height and puts its baseline in it, following the spacing rule.</summary>
    private static void Measure(LineBox line, ParagraphFormat format)
    {
        double ascent = 0;
        double descent = 0;
        double single = 0;

        foreach (InlineFragment fragment in line.Fragments)
        {
            ascent = Math.Max(ascent, fragment.Ascent);
            descent = Math.Max(descent, fragment.Descent);
            single = Math.Max(single, fragment.LineHeight);
        }

        if (line.IsEmpty && line.EmptyStyle is { } empty)
        {
            ascent = empty.Ascent;
            descent = empty.Descent;
            single = empty.LineHeight;
        }

        double natural = Math.Max(single, ascent + descent);
        double height = (format.LineSpacingRule ?? LineSpacingRule.Auto) switch
        {
            LineSpacingRule.Exact when format.LineSpacing is { } exact => exact.Points,
            LineSpacingRule.AtLeast when format.LineSpacing is { } least => Math.Max(least.Points, natural),
            LineSpacingRule.Auto when format.LineSpacing is { } multiple => natural * (multiple.Twips / SingleSpacing),
            _ => natural,
        };

        line.Ascent = ascent;
        line.Descent = descent;
        line.Height = Math.Max(1, height);

        // Extra leading goes above the text, which is where a word processor puts it.
        line.BaselineFromTop = Math.Max(ascent, line.Height - descent);
    }

    /// <summary>
    /// Lines a line up between the indents, stretching its spaces when it is justified. In a
    /// right-to-left paragraph the sides swap: left and right are start and end, and the start
    /// is the right — including the last line of a justified paragraph, which goes to the start.
    /// </summary>
    private static void Align(LineBox line, ParagraphFormat format)
    {
        // Left and right are start and end: the unstated default is the start, and in a
        // right-to-left paragraph the start is the right side, so the sides swap.
        bool rtl = format.RightToLeft == true;
        ParagraphAlignment alignment = format.Alignment ?? ParagraphAlignment.Left;

        if (rtl)
        {
            alignment = alignment switch
            {
                ParagraphAlignment.Left => ParagraphAlignment.Right,
                ParagraphAlignment.Right => ParagraphAlignment.Left,
                _ => alignment,
            };
        }

        double slack = line.AvailableWidth - line.Width;

        if (slack <= 0.01)
            return;

        switch (alignment)
        {
            case ParagraphAlignment.Center:
                Shift(line, slack / 2);
                break;

            case ParagraphAlignment.Right:
            case ParagraphAlignment.Justify when line.IsLastLine && rtl:
                Shift(line, slack);
                break;

            case ParagraphAlignment.Justify when !line.IsLastLine:
            case ParagraphAlignment.Distribute:
            case ParagraphAlignment.ThaiDistribute:
                Justify(line, slack);
                break;

            default:
                break;
        }
    }

    private static void Shift(LineBox line, double offset)
    {
        foreach (InlineFragment fragment in line.Fragments)
            fragment.X += offset;
    }

    /// <summary>
    /// Spreads the slack across the spaces of a line. The fragments after each space move along by
    /// what the spaces before them gained, so the line stays consistent whatever it is made of.
    /// </summary>
    private static void Justify(LineBox line, double slack)
    {
        // The spaces past the last visible character are not stretched: what they gained would
        // hang off the end of the line and leave the text a space short of the margin.
        int spaces = line.SpaceCount - line.TrailingSpaceCount;
        if (spaces <= 0)
            return;

        double extra = slack / spaces;
        line.ExtraSpaceWidth = extra;

        double shift = 0;
        foreach (InlineFragment fragment in line.Fragments)
        {
            fragment.X += shift;
            if (fragment is TextFragment text)
                shift += extra * text.SpaceCount;
        }
    }

    private static double SpaceBefore(ParagraphFormat format, LineBox first)
    {
        if (format.SpacingBeforeAuto == true && format.SpacingBefore is null)
            return AutoSpacing;

        double points = format.SpacingBefore?.Points ?? 0;
        if (format.SpacingBeforeLines is { } lines and > 0)
            points = Math.Max(points, lines / 100.0 * first.Height);

        return Math.Max(0, points);
    }

    private static double SpaceAfter(ParagraphFormat format, LineBox last)
    {
        if (format.SpacingAfterAuto == true && format.SpacingAfter is null)
            return AutoSpacing;

        double points = format.SpacingAfter?.Points ?? 0;
        if (format.SpacingAfterLines is { } lines and > 0)
            points = Math.Max(points, lines / 100.0 * last.Height);

        return Math.Max(0, points);
    }
}
