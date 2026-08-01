using System.Text;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Turns the content of one paragraph into characters: its runs, the objects anchored in it,
/// and the marks and ranges that have no width of their own.
/// </summary>
internal sealed partial class StoryAssembler
{
    /// <summary>Writes the runs of a paragraph, mapping the model's content onto the format's characters.</summary>
    private void WriteContent(Paragraph paragraph)
    {
        Dictionary<int, InlineObject> objects = paragraph.Objects.ToDictionary(static o => o.Offset, static o => o.Object);
        Dictionary<int, List<InlineMark>> marks = [];
        foreach ((int offset, InlineMark mark) in paragraph.Marks)
        {
            if (!marks.TryGetValue(offset, out List<InlineMark>? list))
                marks[offset] = list = [];
            list.Add(mark);
        }

        Dictionary<int, List<InlineRange>> opens = [];
        Dictionary<int, List<InlineRange>> closes = [];
        foreach ((int start, int length, InlineRange range) in paragraph.Ranges)
        {
            // Both of these become the same thing here: a field spelled out as the characters
            // that begin it, name it, separate it from its result and end it.
            if (range is not (Hyperlink or SimpleField))
                continue;
            (opens.TryGetValue(start, out List<InlineRange>? o) ? o : opens[start] = []).Add(range);
            (closes.TryGetValue(start + length, out List<InlineRange>? c) ? c : closes[start + length] = []).Add(range);
        }

        ReadOnlySpan<char> text = paragraph.AsSpan();
        foreach (Run run in paragraph.Runs)
        {
            byte[] properties = _context.BuildRun(run.Format);
            var pending = new StringBuilder();
            int pendingStart = Position;

            for (int i = run.Start; i < run.Start + run.Length; i++)
            {
                if (closes.TryGetValue(i, out List<InlineRange>? ending))
                {
                    Flush(pending, ref pendingStart, properties);
                    for (int r = ending.Count - 1; r >= 0; r--)
                        _context.CloseField(this, ending[r], run.Format);
                    pendingStart = Position;
                }

                if (opens.TryGetValue(i, out List<InlineRange>? starting))
                {
                    Flush(pending, ref pendingStart, properties);
                    foreach (InlineRange range in starting)
                        _context.OpenField(this, range, run.Format);
                    pendingStart = Position;
                }

                if (marks.TryGetValue(i, out List<InlineMark>? here))
                {
                    Flush(pending, ref pendingStart, properties);
                    foreach (InlineMark mark in here)
                        _context.NoteMark(mark, Position);
                }

                if (objects.TryGetValue(i, out InlineObject? anchored))
                {
                    Flush(pending, ref pendingStart, properties);
                    WriteObject(anchored, run.Format);
                    pendingStart = Position;
                    continue;
                }

                if (Map(text[i]) is { } mapped)
                    pending.Append(mapped);
            }

            Flush(pending, ref pendingStart, properties);
        }

        // Anchors that sit at the very end of the paragraph have no run to ride along with.
        if (closes.TryGetValue(paragraph.TextLength, out List<InlineRange>? last))
        {
            for (int r = last.Count - 1; r >= 0; r--)
                _context.CloseField(this, last[r], RunFormat.Default);
        }

        if (marks.TryGetValue(paragraph.TextLength, out List<InlineMark>? trailing))
        {
            foreach (InlineMark mark in trailing)
                _context.NoteMark(mark, Position);
        }
    }

    private void Flush(StringBuilder pending, ref int start, byte[] properties)
    {
        if (pending.Length == 0)
        {
            start = Position;
            return;
        }

        _text.Append(pending);
        _runs.Add(new RunSpanRecord(start, Position, properties));
        pending.Clear();
        start = Position;
    }

    /// <summary>Writes one anchored object as the reserved character that stands for it.</summary>
    private void WriteObject(InlineObject anchored, RunFormat format)
    {
        int start = Position;
        switch (anchored)
        {
            case Break { Kind: BreakKind.Page }:
                _text.Append(DocChar.PageBreak);
                _runs.Add(new RunSpanRecord(start, Position, _context.BuildRun(format)));
                return;
            case Break { Kind: BreakKind.Column }:
                _text.Append(DocChar.ColumnBreak);
                _runs.Add(new RunSpanRecord(start, Position, _context.BuildRun(format)));
                return;
            case Break:
                _text.Append(DocChar.LineBreak);
                _runs.Add(new RunSpanRecord(start, Position, _context.BuildRun(format)));
                return;
            default:
                _context.WriteObject(this, anchored, format);
                return;
        }
    }

    /// <summary>Appends a reserved character and gives it the run properties it needs.</summary>
    internal void WriteSpecial(char value, byte[] properties)
    {
        int start = Position;
        _text.Append(value);
        _runs.Add(new RunSpanRecord(start, Position, properties));
    }

    /// <summary>
    /// Adds more property modifiers to the paragraph just written. A row mark carries the
    /// row's properties, and those are only known once the mark itself has been placed.
    /// </summary>
    internal void AppendToLastParagraph(byte[] extra)
    {
        if (_paragraphs.Count == 0 || extra.Length == 0)
            return;

        ParagraphSpan last = _paragraphs[^1];
        _paragraphs[^1] = last with { Properties = [.. last.Properties, .. extra] };
    }

    /// <summary>Appends plain text under one set of character properties.</summary>
    internal void WriteText(string value, byte[] properties)
    {
        if (value.Length == 0)
            return;

        int start = Position;
        foreach (char c in value)
        {
            if (Map(c) is { } mapped)
                _text.Append(mapped);
        }

        if (Position > start)
            _runs.Add(new RunSpanRecord(start, Position, properties));
    }

    /// <summary>
    /// Maps a character of the model onto the character the binary format uses for it, or
    /// drops it when the format has no equivalent.
    /// </summary>
    private static char? Map(char value) => value switch
    {
        '\n' => DocChar.LineBreak,
        '\t' => DocChar.Tab,
        '\u00AD' => DocChar.OptionalHyphen,
        '\u2011' => DocChar.NonBreakingHyphen,
        '\r' or InlineObject.Placeholder => null,
        < ' ' and not '\t' => null,
        _ => value,
    };
}
