using System.Globalization;
using Quillwright.Model;
using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Turns the offset-based content of a paragraph back into nested WordprocessingML.
/// </summary>
/// <remarks>
/// The paragraph stores runs, wrappers and marks as ranges over one text buffer. Writing
/// walks the offsets in ascending order: at every boundary it closes the wrappers that end
/// there, emits the marks that sit there, opens the wrappers that start there, and then
/// writes the stretch of run content up to the next boundary. A stack of open wrappers
/// re-creates the nesting the schema requires. Every cursor moves forward only, so a
/// paragraph is written in one pass over its content.
/// </remarks>
internal partial struct ParagraphEmitter
{
    private readonly Utf8XmlWriter _writer;
    private readonly Paragraph _paragraph;
    private readonly BodyWriteContext _context;
    private readonly List<AnchoredRange> _ranges;
    private readonly List<AnchoredMark> _marks;
    private readonly List<AnchoredObject> _objects;
    private readonly Stack<AnchoredRange> _open;
    private int _nextRange;
    private int _nextMark;
    private int _nextObject;
    private int _runIndex;
    private bool _runOpen;

    public ParagraphEmitter(Utf8XmlWriter writer, Paragraph paragraph, BodyWriteContext context)
    {
        _writer = writer;
        _paragraph = paragraph;
        _context = context;
        _marks = paragraph.MarkList ?? [];
        _objects = paragraph.ObjectList ?? [];
        _open = new Stack<AnchoredRange>();

        // Outer wrappers must open before inner ones, so equal starts sort by descending length.
        _ranges = paragraph.RangeList is null ? [] : [.. paragraph.RangeList];
        _ranges.Sort(static (left, right) =>
            left.Start != right.Start ? left.Start.CompareTo(right.Start) : right.Length.CompareTo(left.Length));
    }

    /// <summary>Writes the whole content of the paragraph.</summary>
    public void Emit()
    {
        int length = _paragraph.TextLength;
        int position = 0;

        while (true)
        {
            while (CloseRangesEndingAt(position) | EmitMarksAt(position) | OpenRangesStartingAt(position))
            {
                // A zero-length wrapper opens and closes at the same offset, so the boundary
                // work repeats until nothing more happens here.
            }

            if (position >= length)
                break;

            int next = NextBoundary(position, length);
            EmitRunContent(position, next);
            position = next;
        }

        while (_open.Count > 0)
            CloseRange(_open.Pop());
    }

    private bool CloseRangesEndingAt(int position)
    {
        bool closed = false;
        while (_open.Count > 0 && _open.Peek().End <= position)
        {
            CloseRun();
            CloseRange(_open.Pop());
            closed = true;
        }

        return closed;
    }

    private bool EmitMarksAt(int position)
    {
        bool emitted = false;
        while (_nextMark < _marks.Count && _marks[_nextMark].Offset <= position)
        {
            CloseRun();
            WriteMark(_marks[_nextMark].Mark);
            _nextMark++;
            emitted = true;
        }

        return emitted;
    }

    private bool OpenRangesStartingAt(int position)
    {
        bool opened = false;
        while (_nextRange < _ranges.Count && _ranges[_nextRange].Start <= position)
        {
            AnchoredRange range = _ranges[_nextRange];
            _nextRange++;
            CloseRun();
            WriteRangePrefix(range.Range);
            _open.Push(range);
            opened = true;
        }

        return opened;
    }

    private int NextBoundary(int position, int length)
    {
        int next = length;
        while (_runIndex < _paragraph.RunCount && _paragraph.RunSpans[_runIndex].End <= position)
            _runIndex++;
        if (_runIndex < _paragraph.RunCount)
            next = Math.Min(next, _paragraph.RunSpans[_runIndex].End);

        if (_nextMark < _marks.Count)
            next = Math.Min(next, _marks[_nextMark].Offset);
        if (_nextRange < _ranges.Count)
            next = Math.Min(next, _ranges[_nextRange].Start);
        if (_open.Count > 0)
            next = Math.Min(next, _open.Peek().End);

        return Math.Max(next, position + 1);
    }

    private void EmitRunContent(int from, int to)
    {
        RunSpan run = _runIndex < _paragraph.RunCount && _paragraph.RunSpans[_runIndex].End > from
            ? _paragraph.RunSpans[_runIndex]
            : new RunSpan { Start = from, Length = 0, Format = RunFormat.Default, Kind = RunKind.Text };

        ReadOnlySpan<char> text = _paragraph.AsSpan();
        int textStart = from;

        for (int i = from; i < to; i++)
        {
            char c = text[i];
            InlineObject? anchored = ObjectAt(i);
            if (anchored is null && c is not ('\t' or '\n' or '\u00AD' or '\u2011' or InlineObject.Placeholder))
                continue;

            FlushText(run, text[textStart..i]);
            if (anchored is { IsRunChild: false })
            {
                CloseRun();
                WriteObject(anchored, c);
            }
            else
            {
                OpenRun(run);
                WriteObject(anchored, c);
            }

            textStart = i + 1;
        }

        FlushText(run, text[textStart..to]);
        CloseRun();
    }

    private InlineObject? ObjectAt(int offset)
    {
        while (_nextObject < _objects.Count && _objects[_nextObject].Offset < offset)
            _nextObject++;
        return _nextObject < _objects.Count && _objects[_nextObject].Offset == offset ? _objects[_nextObject].Object : null;
    }

    private void FlushText(RunSpan run, scoped ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return;

        OpenRun(run);
        ReadOnlySpan<byte> name = run.Kind switch
        {
            RunKind.FieldInstruction => "instrText"u8,
            RunKind.Deleted => "delText"u8,
            RunKind.DeletedFieldInstruction => "delInstrText"u8,
            _ => "t"u8,
        };

        WordXml.Open(_writer, name);
        _writer.WriteRaw(" xml:space=\"preserve\">"u8);
        _writer.WriteText(text);
        WordXml.Close(_writer, name);
    }

    private void OpenRun(RunSpan run)
    {
        if (_runOpen)
            return;

        _writer.WriteRaw("<w:r"u8);
        if (run.Attributes is { } attributes)
            _writer.WriteRawXml(attributes);
        _writer.WriteRaw(">"u8);
        RunFormatWriter.Write(_writer, run.Format);
        _runOpen = true;
    }

    private void CloseRun()
    {
        if (!_runOpen)
            return;
        _writer.WriteRaw("</w:r>"u8);
        _runOpen = false;
    }
}
