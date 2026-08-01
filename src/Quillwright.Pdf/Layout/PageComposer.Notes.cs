using Inkwright;
using Quillwright.Model;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Footnotes and endnotes.
/// </summary>
/// <remarks>
/// A footnote makes pagination circular: the room left for the text on a page depends on how much
/// of it the notes have taken, and which notes those are depends on how much text fitted. The way
/// out is the one a typesetter uses — fill the page a line at a time, and when a line brings a
/// note with it, ask whether the line and the note together still fit. If they do not, the line
/// goes to the next page and takes its note along.
/// </remarks>
internal sealed partial class PageComposer
{
    /// <summary>Space above the rule that separates the notes from the text.</summary>
    private const double SeparatorSpaceAbove = 6;

    /// <summary>Space below it.</summary>
    private const double SeparatorSpaceBelow = 4;

    /// <summary>How thick the rule is.</summary>
    private const double SeparatorThickness = 0.5;

    /// <summary>How far across the text the rule runs, as Word draws it.</summary>
    private const double SeparatorFraction = 1.0 / 3;

    private readonly NoteCounter _noteCounter;
    private readonly Dictionary<Note, List<BlockBox>> _noteBodies = [];
    private readonly List<PlacedNote> _pageNotes = [];
    private readonly List<NoteMark> _endnotes = [];

    private double _noteReserve;

    /// <summary>A note measured and waiting for the bottom of the page it belongs to.</summary>
    private sealed record PlacedNote(NoteMark Mark, List<BlockBox> Content, double Height);

    /// <summary>The bottom of the body once the notes of this page have taken their share.</summary>
    private double NoteAwareBottom => Current.Geometry.ContentBottom - _noteReserve;

    /// <summary>Numbers a reference and, for an endnote, remembers it for the end of the document.</summary>
    private NoteMark? NumberNote(NoteReference reference)
    {
        NoteMark? mark = _rehearsing
            ? _noteCounter.Peek(reference, _section)
            : _noteCounter.Next(reference, _section);

        if (!_rehearsing && mark is { IsEndnote: true } endnote)
            _endnotes.Add(endnote);

        return mark;
    }

    /// <summary>
    /// How much more room the notes would take if a line were placed here. A footnote the page
    /// already carries costs nothing more, and the first one on a page pays for the separator too.
    /// </summary>
    private double ReserveFor(LineBox line, double reserve, List<NoteMark>? taken)
    {
        if (line.Notes.Count == 0)
            return 0;

        double added = 0;
        foreach (NoteMark note in line.Notes)
        {
            if (note.IsEndnote || note.Body is null || Carried(note, taken))
                continue;

            if (reserve + added <= 0)
                added += SeparatorHeight;

            added += MeasureNote(note).Height;
            taken?.Add(note);
        }

        return added;
    }

    private bool Carried(NoteMark note, List<NoteMark>? taken)
    {
        foreach (PlacedNote placed in _pageNotes)
        {
            if (ReferenceEquals(placed.Mark.Body, note.Body))
                return true;
        }

        return taken?.Any(candidate => ReferenceEquals(candidate.Body, note.Body)) == true;
    }

    private static double SeparatorHeight => SeparatorSpaceAbove + SeparatorThickness + SeparatorSpaceBelow;

    /// <summary>How much room the notes referenced anywhere in a table row would take.</summary>
    private double RowNoteReserve(RowBox row)
    {
        double added = 0;
        List<NoteMark> owed = [];

        foreach (CellBox cell in row.Cells)
        {
            foreach (BlockBox block in cell.Content)
            {
                if (block is not ParagraphBox paragraph)
                    continue;

                foreach (LineBox line in paragraph.Lines)
                    added += ReserveFor(line, _noteReserve + added, owed);
            }
        }

        return added;
    }

    /// <summary>Puts the notes a run of lines owes onto the page those lines are landing on.</summary>
    private void CommitNotes(ParagraphBox box, int index, int count)
    {
        for (int i = 0; i < count; i++)
        {
            foreach (NoteMark note in box.Lines[index + i].Notes)
            {
                if (note.IsEndnote || note.Body is null || Carried(note, taken: null))
                    continue;

                if (_pageNotes.Count == 0)
                    _noteReserve += SeparatorHeight;

                PlacedNote placed = MeasureNote(note);
                _pageNotes.Add(placed);
                _noteReserve += placed.Height;
            }
        }
    }

    /// <summary>Lays out a note's body once, however many times the pagination asks about it.</summary>
    private PlacedNote MeasureNote(NoteMark note)
    {
        Note body = note.Body!;
        if (!_noteBodies.TryGetValue(body, out List<BlockBox>? content))
        {
            string? outer = _layouter.NoteMark;
            _layouter.NoteMark = note.Number;
            content = MeasureBlocks(body.Blocks, Current.Geometry.ContentWidth);
            _layouter.NoteMark = outer;
            _noteBodies[body] = content;
        }

        double height = 0;
        foreach (BlockBox block in content)
            height += block.TotalHeight;

        return new PlacedNote(note, content, height);
    }

    /// <summary>Draws the notes of the page that is being closed, and empties the tray.</summary>
    private void FlushNotes()
    {
        if (_pageNotes.Count == 0)
        {
            _noteReserve = 0;
            return;
        }

        PageGeometry geometry = Current.Geometry;
        NotePosition position = _pageNotes[0].Mark.Settings.Position;

        // Beneath the text means directly under it, which on a page that ends early is not the
        // bottom of the page; every other placement means the bottom.
        double top = position == NotePosition.BeneathText
            ? Math.Min(_cursor, geometry.ContentBottom - _noteReserve)
            : geometry.ContentBottom - _noteReserve;

        double y = Separator(geometry, top);

        foreach (PlacedNote note in _pageNotes)
            y = DrawBlocks(note.Content, geometry.ContentLeft, y);

        _pageNotes.Clear();
        _noteReserve = 0;
    }

    /// <summary>
    /// Puts the endnotes where they belong: after everything else, under a separator, flowing over
    /// as many pages as they need. They are ordinary content by the time they get here.
    /// </summary>
    private void FlowEndnotes()
    {
        if (_endnotes.Count == 0)
            return;

        if (_endnotes[0].Settings.Position == NotePosition.SectionEnd)
        {
            _context.Diagnostics.Add(
                PdfExportWarningKind.LayoutApproximated,
                "Endnotes collected at the end of each section are collected at the end of the document instead.",
                "endnote-position");
        }

        if (_pageHasContent)
            NewPage();
        else
            FlushNotes();

        PageGeometry geometry = Current.Geometry;
        _cursor = Separator(geometry, _cursor);
        MarkFilled();

        foreach (NoteMark note in _endnotes)
        {
            if (note.Body is null)
                continue;

            string? outer = _layouter.NoteMark;
            _layouter.NoteMark = note.Number;
            Flow(note.Body.Blocks);
            _layouter.NoteMark = outer;
        }
    }

    /// <summary>Draws the rule that tells a reader the notes have begun.</summary>
    private double Separator(PageGeometry geometry, double top)
    {
        double y = top + SeparatorSpaceAbove;

        Current.Items.Add(new StrokeItem
        {
            X = geometry.ContentLeft,
            Y = y,
            X2 = geometry.ContentLeft + (geometry.ContentWidth * SeparatorFraction),
            Y2 = y,
            Thickness = SeparatorThickness,
            Color = PdfColor.Black,
        });

        return y + SeparatorThickness + SeparatorSpaceBelow;
    }
}
