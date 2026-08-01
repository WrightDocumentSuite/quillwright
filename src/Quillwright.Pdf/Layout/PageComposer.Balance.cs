namespace Quillwright.Pdf.Layout;

/// <summary>
/// Balancing the columns of a section a continuous break ends (ISO/IEC 29500-1 §17.18.5: the
/// next section starts at the minimum height at which the content before it is laid out).
/// </summary>
/// <remarks>
/// The section's blocks were measured once and their boxes recorded as they were placed. To
/// balance, the placement — and only the placement — is undone back to the start of the band and
/// played again with an artificial bottom at a fraction of the content's height, so the columns
/// come out even. Measuring is what moves the counters, and nothing here measures.
/// </remarks>
internal sealed partial class PageComposer
{
    /// <summary>The boxes of the section being laid out, in placement order, when recording.</summary>
    private List<BlockBox>? _reel;

    /// <summary>The page the current section started on.</summary>
    private int _sectionPage;

    /// <summary>An artificial band bottom, in force while a balanced band is being replayed.</summary>
    private double? _balanceBottom;

    /// <summary>The state of the band as it began, which is what a replay rewinds to.</summary>
    private BandCheckpoint _bandStart;

    private readonly record struct BandCheckpoint(
        int Pages,
        int NextNumber,
        int Items,
        int Furniture,
        double Cursor,
        int Column,
        double[] Depths,
        bool HasContent,
        bool PageHasContent,
        int PageNotes,
        double NoteReserve,
        int Obstacles);

    /// <summary>Remembers the state the band begins in, for the replay that balances it.</summary>
    private void MarkBandStart() => _bandStart = new BandCheckpoint(
        _pages.Count,
        _nextNumber,
        Current.Items.Count,
        Current.Furniture.Count,
        _cursor,
        _column,
        [.. _columnDepth],
        _hasContent,
        _pageHasContent,
        _pageNotes.Count,
        _noteReserve,
        _wrapObstacles.Count);

    /// <summary>
    /// Balances the band a continuous break is about to close. Only the plain case is balanced —
    /// a section of paragraphs that fitted one band of one page, with no notes pending — and
    /// everything else keeps the columns it filled, which is never wrong, only uneven.
    /// </summary>
    private void BalanceBand(List<BlockBox> reel)
    {
        if (_columns.Count <= 1 || reel.Count == 0 || _pages.Count != _sectionPage)
            return;

        if (_pageNotes.Count > _bandStart.PageNotes)
            return;

        foreach (BlockBox block in reel)
        {
            if (block is not ParagraphBox)
                return;
        }

        _columnDepth[_column] = Math.Max(_columnDepth[_column], _cursor);
        double total = 0;
        for (int i = 0; i < _columns.Count; i++)
            total += Math.Max(0, _columnDepth[i] - _bandTop);

        if (total <= 0)
            return;

        // The first target is the even share; a second try adds slack for the lines that do not
        // split evenly; the last try gives up and plays the original layout back.
        Span<double> targets = [total / _columns.Count, total / _columns.Count * 1.2, double.MaxValue];

        foreach (double target in targets)
        {
            BandCheckpoint mark = _bandStart;
            Rewind(mark);
            _balanceBottom = target == double.MaxValue ? null : _bandTop + target + 0.5;
            Replay(reel);
            _balanceBottom = null;

            if (_pages.Count == mark.Pages)
                return;
        }
    }

    /// <summary>Puts the composer back where the band began, undoing everything placed since.</summary>
    private void Rewind(in BandCheckpoint mark)
    {
        while (_pages.Count > mark.Pages)
            _pages.RemoveAt(_pages.Count - 1);

        _page = _pages[^1];
        _nextNumber = mark.NextNumber;
        Current.Items.RemoveRange(mark.Items, Current.Items.Count - mark.Items);
        Current.Furniture.RemoveRange(mark.Furniture, Current.Furniture.Count - mark.Furniture);
        _cursor = mark.Cursor;
        _column = mark.Column;
        _columnDepth = [.. mark.Depths];
        _hasContent = mark.HasContent;
        _pageHasContent = mark.PageHasContent;
        _noteReserve = mark.NoteReserve;

        while (_pageNotes.Count > mark.PageNotes)
            _pageNotes.RemoveAt(_pageNotes.Count - 1);

        while (_wrapObstacles.Count > mark.Obstacles)
            _wrapObstacles.RemoveAt(_wrapObstacles.Count - 1);
    }

    /// <summary>
    /// Places the recorded boxes again, keeping together what asked to stay together. The boxes
    /// were measured by the first pass; playing them back moves no counter.
    /// </summary>
    private void Replay(List<BlockBox> reel)
    {
        ParagraphBox? previous = null;

        for (int index = 0; index < reel.Count;)
        {
            var group = new List<ParagraphBox> { (ParagraphBox)reel[index] };
            while (group[^1].KeepWithNext && index + group.Count < reel.Count && group.Count < 64)
                group.Add((ParagraphBox)reel[index + group.Count]);

            if (group.Count > 1)
                MakeRoomFor(group);

            foreach (ParagraphBox box in group)
            {
                PlaceParagraph(box, previous);
                previous = box;
            }

            index += group.Count;
        }
    }
}
