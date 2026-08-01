using Inkwright;
using Quillwright.Model;
using Quillwright.Pdf.Render;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Decides how the document falls onto pages: opens a page, fills it from the top, and opens the
/// next one when what comes next will not fit.
/// </summary>
/// <remarks>
/// The composer owns the vertical cursor and nothing else owns it, so every decision about where a
/// break lands is made in one place. Blocks are measured elsewhere and handed here as pieces —
/// lines, table rows — which the composer either places or postpones to a fresh page.
/// </remarks>
internal sealed partial class PageComposer
{
    private readonly PdfExportContext _context;
    private readonly PageFieldResolver _fields;
    private readonly TextMeasurer _measurer;
    private readonly ParagraphLayouter _layouter;
    private readonly List<ComposedPage> _pages = [];
    private readonly Dictionary<string, BookmarkTarget> _bookmarks = new(StringComparer.Ordinal);

    private ComposedPage? _page;
    private double _cursor;
    private int _nextNumber = 1;
    private bool _hasContent;

    /// <summary>
    /// Set while a paragraph is being measured only to find out how wide it wants to be. Nothing
    /// measured then reaches a page, so nothing measured then may move a counter.
    /// </summary>
    private bool _rehearsing;

    internal PageComposer(PdfExportContext context, PageFieldResolver fields)
    {
        _context = context;
        _fields = fields;
        _measurer = new TextMeasurer(context);
        _numbering = new NumberingCounter(context.Source.Numbering);
        _layouter = new ParagraphLayouter(context, _measurer)
        {
            EstimateField = (kind, format, bookmark) => _fields.Estimate(kind, _page?.Number ?? 1, format, bookmark),
            Prefix = NumberPrefix,
            Notes = NumberNote,
        };

        _tables = new TableLayouter(context, _layouter, rehearsing => _rehearsing = rehearsing);
        _noteCounter = new NoteCounter(context.Source, context.Diagnostics);
    }

    /// <summary>Where each bookmark of the document ended up, once it has been composed.</summary>
    public IReadOnlyDictionary<string, BookmarkTarget> Bookmarks => _bookmarks;

    /// <summary>Lays the whole document out.</summary>
    public IReadOnlyList<ComposedPage> Compose()
    {
        ResetNumbering();
        IReadOnlyList<Section> sections = _context.Source.Sections;

        for (int index = 0; index < sections.Count; index++)
        {
            StartSection(sections[index]);
            _sectionPage = _pages.Count;
            _reel = [];
            Flow(sections[index].Blocks);

            List<BlockBox> reel = _reel;
            _reel = null;

            // A continuous break balances the section it ends (ISO/IEC 29500-1 §17.18.5).
            if (index + 1 < sections.Count && sections[index + 1].Properties.Start == SectionStart.Continuous)
                BalanceBand(reel);
        }

        if (_pages.Count == 0)
            StartPage(_context.Source.Sections[0], sectionStart: true);

        FlowEndnotes();
        ClosePage();
        FinishPages();
        return _pages;
    }

    private ComposedPage Current => _page ?? StartPage(_context.Source.Sections[0], sectionStart: true);
    private void PlaceOther(Block block)
    {
        switch (block)
        {
            case Table table:
                TableBox measured = _tables.Measure(table, CurrentWidth);
                _reel?.Add(measured);
                PlaceTable(measured);
                break;

            case RawBlock:
                _context.Diagnostics.Add(
                    PdfExportWarningKind.ContentSkipped,
                    "Block-level content the model keeps verbatim is not drawn.",
                    "raw-block");
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Puts a measured paragraph on the page, breaking it across columns and pages when it has to.
    /// A paragraph that lands where something floats is measured again around it first.
    /// </summary>
    private void PlaceParagraph(ParagraphBox box, ParagraphBox? previous)
    {
        if (box.PageBreakBefore && _pageHasContent)
            NewPage();

        double before = Spacing(box, previous);
        double y = _cursor + (_hasContent ? before : 0);
        int index = 0;
        ParagraphBox clean = box;

        // The leads a shape put into the lines hold only while the lines sit where the shape was
        // fitted. A chunk carried to another column leaves the floats behind, and their leads too.
        bool leads = true;

        while (index < box.Lines.Count)
        {
            if (box.Lines[index].StartsNewPage && (index > 0 || _pageHasContent))
            {
                NewPage();
                y = _cursor;
                leads &= index == 0;
            }
            else if (box.Lines[index].StartsNewColumn && (index > 0 || _hasContent))
            {
                NewColumn();
                y = _cursor;
                leads &= index == 0;
            }

            // Only now is it known where the paragraph starts, which is what wrapping hangs on.
            if (index == 0)
            {
                box = ShapeFor(clean, y) is { } shape
                    ? _layouter.Layout(clean.Source, CurrentWidth, shape, clean)
                    : clean;
            }

            int take = Take(box, index, y, leads);
            if (take == 0)
            {
                NewColumn();
                y = _cursor;
                continue;
            }

            if (index == 0)
                RecordBookmarks(box.Source, CurrentLeft, y);

            CommitNotes(box, index, take);

            double height = Height(box, index, take, leads);
            Decorate(box, index, take, CurrentLeft, y, height);

            if (index == 0)
            {
                PlaceFloats(box, CurrentLeft, y);
                PlaceFloatingShapes(box, CurrentLeft, y);
            }

            // Row by row: the boxes of one row share a vertical position and advance it once.
            for (int i = index; i < index + take;)
            {
                int end = Math.Min(LineRows.End(box.Lines, i), index + take);
                y += leads ? box.Lines[i].Lead : 0;

                for (int s = i; s < end; s++)
                    PlaceLine(box, box.Lines[s], CurrentLeft, y);

                y += LineRows.Height(box.Lines, i, end);
                i = end;
            }

            index += take;
            MarkFilled();

            // A line that carries an explicit break opens its own column or page at the top of
            // the loop; anything else that remains simply ran out of room here.
            if (index < box.Lines.Count && !box.Lines[index].StartsNewPage && !box.Lines[index].StartsNewColumn)
            {
                NewColumn();
                y = _cursor;
                leads = false;
            }
        }

        _cursor = y + box.SpacingAfter;
    }

    /// <summary>
    /// How many of a paragraph's remaining lines belong on this page, once the room left and the
    /// paragraph's own wishes about being broken have both been taken into account.
    /// </summary>
    /// <param name="box">The paragraph being placed.</param>
    /// <param name="index">The first line not yet placed.</param>
    /// <param name="y">Where that line would start.</param>
    /// <param name="leads">Whether the leads the lines carry still hold where the chunk sits.</param>
    private int Take(ParagraphBox box, int index, double y, bool leads)
    {
        double bottom = Math.Min(Current.Geometry.ContentBottom, _balanceBottom ?? double.MaxValue);
        double reserve = _noteReserve;
        List<NoteMark> owed = [];
        int fit = 0;
        double used = y;

        while (index + fit < box.Lines.Count)
        {
            // A row moves whole, so it is offered whole: the boxes of one row share a vertical
            // position, and each that owes a note needs room for the note as well as for itself.
            int end = LineRows.End(box.Lines, index + fit);
            double added = 0;
            for (int s = index + fit; s < end; s++)
                added += ReserveFor(box.Lines[s], reserve + added, owed);

            double advance = (leads ? box.Lines[index + fit].Lead : 0)
                + LineRows.Height(box.Lines, index + fit, end);
            if (used + advance > bottom - reserve - added + 0.01)
                break;

            used += advance;
            reserve += added;
            fit = end - index;

            // An explicit break inside the paragraph ends the column here whatever room is left.
            if (index + fit < box.Lines.Count && Breaks(box.Lines[index + fit]))
                break;
        }

        int remaining = box.Lines.Count - index;

        // A column with nothing in it must accept something, or the same row would be postponed
        // for ever onto columns it will never fit.
        if (!_hasContent)
        {
            if (!_columns.IsUniform && fit > 0 && fit < remaining)
            {
                _context.Diagnostics.Add(
                    PdfExportWarningKind.LayoutApproximated,
                    "A paragraph split between columns of unequal width keeps the lines it was measured " +
                    "with, so they do not refit the column they continue in.",
                    "column-widths");
            }

            return Math.Max(LineRows.End(box.Lines, index) - index, fit);
        }

        if (fit == 0)
            return 0;

        // The author chose this break, so neither keeping nor widow control gets a say in it.
        if (fit < remaining && Breaks(box.Lines[index + fit]))
            return fit;

        // Between unequal columns a paragraph moves whole: its lines were measured against this
        // column and would not fit the next one honestly.
        if (!_columns.IsUniform && fit < remaining)
            return 0;

        if (box.KeepLinesTogether && fit < remaining)
            return 0;

        if (!box.WidowControl || fit >= remaining)
            return fit;

        // Two rows is the smallest piece worth leaving on either side of a break.
        if (index == 0 && LineRows.End(box.Lines, index) - index == fit && remaining > fit)
            return 0;

        // Do not strand the last row alone: give one row back when more than one was taken.
        int lastRowStart = LineRows.Start(box.Lines, index + fit - 1) - index;
        if (LineRows.End(box.Lines, index + fit) == box.Lines.Count && lastRowStart > 0)
            return lastRowStart;

        return fit;
    }

    /// <summary>Whether a line insists on opening a new column or page.</summary>
    private static bool Breaks(LineBox line) => line.StartsNewPage || line.StartsNewColumn;

    private static double Height(ParagraphBox box, int index, int count, bool leads)
    {
        double total = 0;
        for (int i = index; i < index + count;)
        {
            int end = Math.Min(LineRows.End(box.Lines, i), index + count);
            total += (leads ? box.Lines[i].Lead : 0) + LineRows.Height(box.Lines, i, end);
            i = end;
        }

        return total;
    }

    /// <summary>
    /// The space above a paragraph. Contextual spacing drops it between neighbours of one style,
    /// which is what keeps the items of a list from drifting apart.
    /// </summary>
    private static double Spacing(ParagraphBox box, ParagraphBox? previous)
    {
        if (previous is null)
            return box.SpacingBefore;

        bool sameStyle = string.Equals(box.Format.StyleId, previous.Format.StyleId, StringComparison.Ordinal);
        if (sameStyle && (box.ContextualSpacing || previous.ContextualSpacing))
            return 0;

        // Word does not add the two spaces together: the larger of the pair is the gap.
        return Math.Max(0, box.SpacingBefore - previous.SpacingAfter);
    }

    private void PlaceLine(ParagraphBox box, LineBox line, double left, double y)
    {
        double x = left + line.IndentLeft;
        TagRef? tag = TagOf(box);

        foreach (InlineFragment fragment in line.Fragments)
        {
            if (fragment is ImageFragment image)
                fragment.Tag = FigureTag(image.Picture, tag);
        }

        Current.Items.Add(new TextLineItem { Line = line, X = x, Y = y, Tag = tag });
        AddLinks(line, x, y);
        DrawInlineShapes(line, x, y);
    }

    /// <summary>Turns the links a line carries into clickable areas over the fragments they cover.</summary>
    private void AddLinks(LineBox line, double x, double y)
    {
        Hyperlink? open = null;
        double start = 0;
        double end = 0;

        foreach (InlineFragment fragment in line.Fragments)
        {
            if (!ReferenceEquals(fragment.Link, open))
            {
                Emit(open, start, end);
                open = fragment.Link;
                start = fragment.X;
            }

            end = fragment.X + fragment.Width;
        }

        Emit(open, start, end);

        void Emit(Hyperlink? link, double from, double to)
        {
            if (link is null || to <= from || (link.Url is null && link.Anchor is null))
                return;

            Current.Items.Add(new LinkItem
            {
                Url = link.Url,
                Anchor = link.Anchor,
                X = x + from,
                Y = y + line.BaselineFromTop - line.Ascent,
                Width = to - from,
                Height = line.Ascent + line.Descent,
            });
        }
    }

    /// <summary>
    /// Notes where the bookmarks of a paragraph landed, so the links that point at them can be
    /// resolved once every page exists.
    /// </summary>
    private void RecordBookmarks(Paragraph paragraph, double left, double y)
    {
        foreach ((int _, InlineMark mark) in paragraph.Marks)
        {
            // Word leaves its own cursor bookmark in every document it saves; nothing points at it.
            if (mark is not BookmarkStart { Name: { Length: > 0 } name } || name == "_GoBack")
                continue;

            _bookmarks.TryAdd(name, new BookmarkTarget(Current.Index, left, Current.Geometry.ToPdfY(y)));
        }
    }
}
