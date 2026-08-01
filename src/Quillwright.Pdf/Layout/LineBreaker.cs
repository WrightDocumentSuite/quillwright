using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Packs a paragraph's content into lines, breaking where the text allows and the width demands.
/// </summary>
/// <remarks>
/// The rule is the ordinary one: a line fills until the next unbreakable piece will not fit, and
/// the trailing spaces of a line do not count towards its width, so a full line does not wrap
/// because of the space after its last word. A single piece too wide even for an empty line is
/// split character by character, which is what a word processor does with a long URL.
/// </remarks>
internal sealed partial class LineBreaker
{
    /// <summary>Characters after which a line may break, beyond the ordinary space.</summary>
    private const string BreakAfter = "-\u2013\u2014\u00AD\u200B/";

    /// <summary>The character that marks a break opportunity and shows only when it is taken.</summary>
    private const char SoftHyphen = '\u00AD';

    private readonly ParagraphMetrics _metrics;
    private readonly List<LineBox> _lines = [];
    private readonly CharacterStyle _defaultStyle;
    private readonly FlowShape? _shape;
    private readonly Func<LineBox, double>? _heightOf;
    private readonly Hyphenator? _hyphenator;

    private LineBox _line = null!;
    private double _x;
    private double _y;
    private double _probe;
    private PendingTab? _pending;
    private InlineItem? _softBreak;
    private IReadOnlyList<FlowBand> _segments = [];
    private int _segment;
    private double _stripLead;
    private double _stripTallest;

    internal LineBreaker(
        ParagraphMetrics metrics,
        CharacterStyle defaultStyle,
        FlowShape? shape = null,
        Func<LineBox, double>? heightOf = null,
        double probeHeight = 0,
        Hyphenator? hyphenator = null)
    {
        _metrics = metrics;
        _defaultStyle = defaultStyle;
        _shape = shape;
        _heightOf = heightOf;
        _probe = probeHeight;
        _hyphenator = hyphenator;
        StartLine();
    }

    /// <summary>Adds one piece of content.</summary>
    /// <param name="item">The piece to place.</param>
    public void Add(InlineItem item)
    {
        // Anything that is not text ends the reach of a soft hyphen: a line that breaks at a
        // tab or a picture did not break at the hyphen, so the hyphen does not show.
        if (item.Kind != InlineKind.Text)
            _softBreak = null;

        switch (item.Kind)
        {
            case InlineKind.Text:
                AddText(item);
                break;

            case InlineKind.Tab:
                AddTab(item);
                break;

            case InlineKind.LineBreak:
                _line.EmptyStyle ??= item.Style;
                BreakStrip();
                break;

            case InlineKind.PageBreak or InlineKind.ColumnBreak:
                // The break ends the line only when there is something on it: a paragraph that
                // opens with a page break starts on the new page rather than leaving a blank line.
                _line.EmptyStyle ??= item.Style;
                if (_line.Fragments.Count > 0)
                    BreakStrip();

                if (item.Kind == InlineKind.ColumnBreak)
                    _line.StartsNewColumn = true;
                else
                    _line.StartsNewPage = true;

                break;

            case InlineKind.Picture:
                AddPicture(item);
                break;

            case InlineKind.Shape:
                AddShape(item);
                break;

            case InlineKind.Equation:
                AddEquation(item);
                break;

            case InlineKind.Chart:
                AddChart(item);
                break;

            case InlineKind.PageField:
                AddPageField(item);
                break;

            case InlineKind.NoteReference:
                AddNoteReference(item);
                break;

            default:
                break;
        }
    }

    /// <summary>Closes the last line and hands back everything.</summary>
    public List<LineBox> Finish()
    {
        // A soft hyphen at the very end of the content never shows: the paragraph ends, the
        // line does not break.
        _softBreak = null;
        Close(_line);
        _lines.Add(_line);

        // The whole last row is last: justification must not stretch either side of it.
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            _lines[i].IsLastLine = true;
            if (!_lines[i].JoinsPrevious)
                break;
        }

        return _lines;
    }

    /// <summary>What the estimate of a page field should be; supplied by the composer.</summary>
    public required Func<PageFieldKind, ListNumberFormat, string?, string> EstimateField { get; init; }

    /// <summary>Starts a fresh row, fitted to whatever floats over the place it will sit.</summary>
    private void StartLine()
    {
        double indent = _metrics.IndentOf(_lines.Count);
        double width = _metrics.WidthOf(_lines.Count);

        _line = new LineBox
        {
            IndentLeft = indent,
            AvailableWidth = width,
        };

        _segments = [];
        _segment = 0;
        _stripLead = 0;
        _stripTallest = 0;

        // A shape narrows the row to the stretches the floats leave it, or pushes it below them.
        if (_shape is { } shape)
        {
            BandFit band = shape.Fit(_y, Math.Max(1, _probe), indent, indent + width);
            _segments = band.Segments;
            _stripLead = band.Lead;
            _line.Lead = band.Lead;
            _line.IndentLeft = _segments[0].Left;
            _line.AvailableWidth = _segments[0].Width;
        }

        _x = 0;
    }

    /// <summary>Continues the same row on the far side of whatever splits it.</summary>
    private void StartSegment(int next)
    {
        _segment = next;
        _line = new LineBox
        {
            JoinsPrevious = true,
            IndentLeft = _segments[next].Left,
            AvailableWidth = _segments[next].Width,
        };

        _x = 0;
    }

    /// <summary>
    /// The line is full. When the row has another stretch on the far side of a float, the text
    /// continues there; only when the row is spent does it move down.
    /// </summary>
    private void BreakLine()
    {
        // The break lands exactly where a soft hyphen was waiting, so the hyphen shows.
        if (_softBreak is { } marked)
        {
            _softBreak = null;
            Append("-", marked.Style.Measure("-"), marked);
        }

        if (_segment + 1 < _segments.Count)
        {
            CloseSegment();
            StartSegment(_segment + 1);
            return;
        }

        BreakStrip();
    }

    /// <summary>Ends the row as the reader sees it and moves down by the tallest of its parts.</summary>
    private void BreakStrip()
    {
        CloseSegment();

        if (_shape is not null)
        {
            _y += _stripLead + _stripTallest;
            _probe = _stripTallest;
        }

        StartLine();
    }

    /// <summary>
    /// Closes one box of the row. Only a shaped paragraph needs the heights this early; a plain
    /// one gets them all at once afterwards, the way it always did.
    /// </summary>
    private void CloseSegment()
    {
        Close(_line);

        if (_shape is not null && _heightOf is not null)
            _stripTallest = Math.Max(_stripTallest, _heightOf(_line));

        _lines.Add(_line);
    }

    private void Close(LineBox line)
    {
        ResolveTab();
        line.EmptyStyle ??= _defaultStyle;
        line.Width = _x - TrailingSpaceWidth(line);
    }

    /// <summary>
    /// Where a tab stop that is not a plain left jump has been waiting for its text. A centred,
    /// right or decimal stop cannot know how far to jump until it can see what follows it, so the
    /// jump is placed at the stop and corrected once the content is in.
    /// </summary>
    private readonly record struct PendingTab(int Index, double TabStart, double Target, TabAlignment Alignment);

    /// <summary>Corrects the jump of a tab now that the text after it has been measured.</summary>
    private void ResolveTab()
    {
        if (_pending is not { } pending || pending.Index >= _line.Fragments.Count)
        {
            _pending = null;
            return;
        }

        double contentWidth = _x - pending.Target;
        double offset = pending.Alignment switch
        {
            TabAlignment.Center => contentWidth / 2,
            TabAlignment.Right => contentWidth,
            TabAlignment.Decimal or TabAlignment.Number => DecimalOffset(pending.Index),
            _ => 0,
        };

        // The text may not be pulled back past where the tab itself began.
        double start = Math.Max(pending.TabStart, pending.Target - offset);
        double delta = start - pending.Target;
        _pending = null;

        if (Math.Abs(delta) < 0.01)
            return;

        for (int i = pending.Index + 1; i < _line.Fragments.Count; i++)
            _line.Fragments[i].X += delta;

        _line.Fragments[pending.Index].Width = start - pending.TabStart;
        _x += delta;
    }

    /// <summary>How far into the text after a tab its decimal separator sits.</summary>
    private double DecimalOffset(int tabIndex)
    {
        for (int i = tabIndex + 1; i < _line.Fragments.Count; i++)
        {
            if (_line.Fragments[i] is not TextFragment text)
                continue;

            int at = text.Text.IndexOfAny(['.', ',']);
            if (at < 0)
                continue;

            double before = text.Style.Measure(text.Text.AsSpan(0, at));
            return _line.Fragments[i].X - _line.Fragments[tabIndex + 1].X + before;
        }

        // Nothing to align on, so the number ends at the stop, which is what Word settles for.
        return _x - _line.Fragments[tabIndex].X - _line.Fragments[tabIndex].Width;
    }

    private static double TrailingSpaceWidth(LineBox line)
    {
        double width = 0;
        for (int i = line.Fragments.Count - 1; i >= 0; i--)
        {
            if (line.Fragments[i] is not TextFragment text)
                break;

            string content = text.Text;
            int end = content.Length;
            while (end > 0 && content[end - 1] == ' ')
                end--;

            width += text.Style.Measure(content.AsSpan(end));
            if (end > 0)
                break;
        }

        return width;
    }

    private void AddText(InlineItem item)
    {
        foreach (Range range in Segments(item.Text))
        {
            string segment = item.Text[range];
            if (segment.Length == 0)
                continue;

            Place(segment, item);
        }
    }

    /// <summary>
    /// Places one unbreakable segment, wrapping first if it does not fit and splitting it only
    /// when a line of its own would not hold it either.
    /// </summary>
    /// <remarks>
    /// A segment ending in a soft hyphen sheds it here: the character never draws, and what is
    /// left behind is a marker that <see cref="BreakLine"/> turns into a real hyphen if the
    /// line does break at it. The fit check reserves the hyphen's width, so the hyphen that
    /// may be needed is never the thing that does not fit.
    /// </remarks>
    private void Place(string segment, InlineItem item)
    {
        bool soft = segment.Length > 0 && segment[^1] == SoftHyphen;
        if (soft)
            segment = segment[..^1];

        if (segment.Length == 0)
        {
            if (soft && _line.Fragments.Count > 0)
                _softBreak = item;

            return;
        }

        while (true)
        {
            double full = item.Style.Measure(segment);
            double core = item.Style.Measure(segment.AsSpan().TrimEnd(' '));
            double reserve = soft ? item.Style.Measure("-") : 0;

            if (_x + core + reserve > _line.AvailableWidth && _line.Fragments.Count > 0)
            {
                if (SplitAtHyphen(segment, item) is { } rest)
                {
                    segment = rest;
                    continue;
                }

                BreakLine();
            }

            // A leading space on a wrapped line is dropped, exactly as a word processor drops it.
            if (_line.Fragments.Count == 0 && segment.Length > 0 && segment[0] == ' ' && _lines.Count > 0)
            {
                string trimmed = segment.TrimStart(' ');
                if (trimmed.Length == 0)
                    return;

                segment = trimmed;
                full = item.Style.Measure(segment);
                core = item.Style.Measure(segment.AsSpan().TrimEnd(' '));
            }

            if (_line.Fragments.Count == 0 && core > _line.AvailableWidth)
            {
                if (SplitAtHyphen(segment, item) is { } rest)
                {
                    segment = rest;
                    continue;
                }

                Split(segment, item);
                if (soft)
                    _softBreak = item;

                return;
            }

            Append(segment, full, item);
            if (soft)
                _softBreak = item;

            return;
        }
    }

    /// <summary>
    /// Puts the longest head of a word the patterns allow on this line, hyphen and all, and
    /// hands back the remainder — or <see langword="null"/> when no break both exists and fits.
    /// </summary>
    private string? SplitAtHyphen(string segment, InlineItem item)
    {
        if (_hyphenator is null)
            return null;

        int[] breaks = _hyphenator.Opportunities(segment, item.Style.Language);
        if (breaks.Length == 0)
            return null;

        double budget = _line.AvailableWidth - _x;
        double hyphen = item.Style.Measure("-");
        int best = 0;
        foreach (int keep in breaks)
        {
            if (item.Style.Measure(segment.AsSpan(0, keep)) + hyphen <= budget)
                best = keep;
            else
                break;
        }

        if (best == 0)
            return null;

        Append(segment[..best], item.Style.Measure(segment.AsSpan(0, best)), item);
        _softBreak = item;
        BreakLine();
        return segment[best..];
    }

    /// <summary>Splits a segment no line can hold, filling each line as far as it will go.</summary>
    private void Split(string segment, InlineItem item)
    {
        int start = 0;
        while (start < segment.Length)
        {
            int count = 0;
            double width = 0;

            while (start + count < segment.Length)
            {
                int step = char.IsHighSurrogate(segment[start + count]) ? 2 : 1;
                double next = item.Style.Measure(segment.AsSpan(start, count + step));
                if (count > 0 && _x + next > _line.AvailableWidth)
                    break;

                count += step;
                width = next;
            }

            Append(segment.Substring(start, count), width, item);
            start += count;

            if (start < segment.Length)
                BreakLine();
        }
    }

    private void Append(string segment, double width, InlineItem item)
    {
        _softBreak = null;
        int spaces = Count(segment, ' ');

        // Consecutive text in one appearance becomes one fragment, so a paragraph of ordinary prose
        // ends up with one fragment per line rather than one per word. Runs of opposite directions
        // stay apart: the bidi pass moves fragments whole.
        if (_line.Fragments.Count > 0 &&
            _line.Fragments[^1] is TextFragment last &&
            ReferenceEquals(last.Style, item.Style) &&
            ReferenceEquals(last.Link, item.Link) &&
            last.RightToLeft == item.RightToLeft)
        {
            _line.Fragments[^1] = new TextFragment
            {
                Text = last.Text + segment,
                Style = last.Style,
                SpaceCount = last.SpaceCount + spaces,
                X = last.X,
                Width = last.Width + width,
                Ascent = last.Ascent,
                Descent = last.Descent,
                LineHeight = last.LineHeight,
                Link = last.Link,
                RightToLeft = last.RightToLeft,
            };
        }
        else
        {
            _line.Fragments.Add(new TextFragment
            {
                Text = segment,
                Style = item.Style,
                SpaceCount = spaces,
                X = _x,
                Width = width,
                Ascent = item.Style.Ascent,
                Descent = item.Style.Descent,
                LineHeight = item.Style.LineHeight,
                Link = item.Link,
                RightToLeft = item.RightToLeft,
            });
        }

        _x += width;
    }
    /// <summary>
    /// The break opportunities in a string. A segment runs to the end of a space run, or just past
    /// a character text may break after, whichever comes first.
    /// </summary>
    private static IEnumerable<Range> Segments(string text)
    {
        int start = 0;
        int i = 0;

        while (i < text.Length)
        {
            if (text[i] == ' ')
            {
                while (i < text.Length && text[i] == ' ')
                    i++;

                yield return new Range(start, i);
                start = i;
                continue;
            }

            if (BreakAfter.Contains(text[i], StringComparison.Ordinal) && i + 1 > start)
            {
                i++;
                yield return new Range(start, i);
                start = i;
                continue;
            }

            i++;
        }

        if (start < text.Length)
            yield return new Range(start, text.Length);
    }

    private static int Count(string text, char value)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c == value)
                count++;
        }

        return count;
    }
}
