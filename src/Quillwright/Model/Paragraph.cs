using System.Runtime.InteropServices;
using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>
/// A paragraph, stored as one text buffer with formatting laid over it as spans rather than
/// as a list of run objects.
/// </summary>
/// <remarks>
/// <para>
/// WordprocessingML splits text into runs wherever formatting changes, and Word splits it
/// further at every edit, so a sentence typed in one go can arrive as fifteen runs. Keeping
/// the text contiguous and describing formatting as offset ranges makes
/// <see cref="Text"/> free, makes search and replace work across run boundaries without any
/// stitching, and costs one small struct per run instead of an object graph.
/// </para>
/// <para>
/// Everything that is not plain text is anchored to the same offsets: objects occupy exactly
/// one character (a tab is <c>\t</c>, a break is <c>\n</c>, everything else is
/// <see cref="InlineObject.Placeholder"/>), marks sit between characters, and wrappers such
/// as hyperlinks cover a range. Editing the text moves all of them consistently.
/// </para>
/// </remarks>
public sealed partial class Paragraph : Block
{
    private char[] _buffer = [];
    private int _length;
    private string? _cachedText;
    private readonly List<RunSpan> _runs = [];
    private List<AnchoredObject>? _objects;
    private List<AnchoredMark>? _marks;
    private List<AnchoredRange>? _ranges;

    /// <summary>Creates an empty paragraph.</summary>
    public Paragraph()
    {
    }

    /// <summary>Creates a paragraph with one run of text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="format">Character formatting of the run.</param>
    public Paragraph(string text, RunFormat? format = null) => AppendText(text, format);

    /// <summary>Paragraph-level formatting (<c>w:pPr</c>).</summary>
    public ParagraphFormat Format { get; set; } = ParagraphFormat.Default;

    /// <summary>
    /// Character formatting of the paragraph mark (<c>w:pPr/w:rPr</c>). It decides how the
    /// pilcrow looks and, for an empty paragraph, how tall the line is.
    /// </summary>
    public RunFormat MarkFormat { get; set; } = RunFormat.Default;

    /// <summary>Attributes of <c>w:p</c> such as revision and paragraph ids, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>Number of characters in the paragraph, counting one per anchored object.</summary>
    public int TextLength => _length;

    /// <summary>Returns <see langword="true"/> when the paragraph holds no characters.</summary>
    public bool IsEmpty => _length == 0;

    /// <summary>
    /// Whether this paragraph carried the section properties in the loaded package. An empty
    /// carrier remains part of the editable model and round-trips as a paragraph, but pagination
    /// engines may treat its mark as the section break rather than as another visible body line.
    /// </summary>
    public bool IsSectionBreakCarrier { get; internal set; }

    /// <summary>
    /// The characters of the paragraph. Objects appear as their placeholder character, so
    /// offsets in this string line up with every anchor in the paragraph.
    /// </summary>
    /// <remarks>Setting this replaces all content with a single run keeping the first run's formatting.</remarks>
    public string Text
    {
        get => _cachedText ??= new string(_buffer, 0, _length);
        set => SetText(value);
    }

    /// <summary>The characters of the paragraph without copying.</summary>
    public ReadOnlySpan<char> AsSpan() => _buffer.AsSpan(0, _length);

    /// <summary>The runs of the paragraph, in order.</summary>
    public RunCollection Runs => new(this);

    /// <summary>The objects anchored in the paragraph, in order of position.</summary>
    public IEnumerable<(int Offset, InlineObject Object)> Objects =>
        _objects is null ? [] : _objects.Select(static a => (a.Offset, a.Object));

    /// <summary>The zero-width marks in the paragraph, in order of position.</summary>
    public IEnumerable<(int Offset, InlineMark Mark)> Marks =>
        _marks is null ? [] : _marks.Select(static a => (a.Offset, a.Mark));

    /// <summary>The wrappers covering stretches of the paragraph.</summary>
    public IEnumerable<(int Start, int Length, InlineRange Range)> Ranges =>
        _ranges is null ? [] : _ranges.Select(static a => (a.Start, a.Length, a.Range));

    /// <summary>
    /// Section properties carried by this paragraph. Non-<see langword="null"/> only on the
    /// last paragraph of a section that is not the last in the document; the loader moves
    /// them onto the <see cref="Section"/> and the writer puts them back.
    /// </summary>
    internal SectionProperties? SectionBreak { get; set; }

    /// <summary>
    /// The plain text of the paragraph. An object with words of its own — a text box — reads
    /// as those words, in the place it is anchored; one with no textual meaning is dropped, so
    /// extraction never yields stray replacement characters.
    /// </summary>
    public override string GetText()
    {
        ReadOnlySpan<char> span = AsSpan();
        if (span.IndexOf(InlineObject.Placeholder) < 0)
            return Text;

        var builder = new System.Text.StringBuilder(_length);
        int next = 0;
        for (int i = 0; i < _length; i++)
        {
            if (_buffer[i] != InlineObject.Placeholder)
            {
                builder.Append(_buffer[i]);
                continue;
            }

            while (_objects is not null && next < _objects.Count && _objects[next].Offset < i)
                next++;

            if (_objects is not null && next < _objects.Count && _objects[next].Offset == i)
                builder.Append(_objects[next].Object.GetText());
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public override Block Clone()
    {
        var clone = new Paragraph
        {
            Format = Format,
            MarkFormat = MarkFormat,
            Attributes = Attributes,
            _length = _length,
            _buffer = _length == 0 ? [] : _buffer.AsSpan(0, _length).ToArray(),
        };

        clone._runs.AddRange(_runs);
        if (_objects is not null)
            clone._objects = [.. _objects];
        if (_marks is not null)
            clone._marks = [.. _marks];
        if (_ranges is not null)
            clone._ranges = [.. _ranges];
        return clone;
    }

    /// <summary>Removes every character, run, object, mark and wrapper.</summary>
    public void Clear()
    {
        _length = 0;
        _cachedText = null;
        _runs.Clear();
        _objects = null;
        _marks = null;
        _ranges = null;
    }

    /// <summary>Appends text in the given formatting, extending the previous run when it matches.</summary>
    /// <param name="text">The text to append.</param>
    /// <param name="format">Character formatting, or <see langword="null"/> to continue the previous run.</param>
    /// <returns>This paragraph, for chaining.</returns>
    public Paragraph AppendText(ReadOnlySpan<char> text, RunFormat? format = null)
    {
        if (text.IsEmpty)
            return this;

        int start = _length;
        AppendChars(text);
        ExtendOrAddRun(start, text.Length, format, RunKind.Text, null);
        Record(start, text.Length);
        return this;
    }

    /// <summary>Appends an object such as a break, a picture or a field boundary.</summary>
    /// <param name="value">The object to anchor at the end of the paragraph.</param>
    /// <param name="format">Character formatting of the run that carries it.</param>
    /// <returns>This paragraph, for chaining.</returns>
    public Paragraph AppendObject(InlineObject value, RunFormat? format = null)
    {
        int offset = _length;
        AppendChars([value.PlaceholderChar]);
        (_objects ??= []).Add(new AnchoredObject { Offset = offset, Object = value });
        Anchor(value);
        ExtendOrAddRun(offset, 1, format, RunKind.Text, null);
        Record(offset, 1);
        return this;
    }

    /// <summary>Records what was just appended, when the document is recording changes.</summary>
    private void Record(int start, int length)
    {
        if (Document?.ActiveTracking is { } tracking)
            Editing.RevisionRecorder.Inserted(this, tracking, start, length);
    }

    /// <summary>Appends a tab character.</summary>
    /// <param name="format">Character formatting of the run that carries it.</param>
    public Paragraph AppendTab(RunFormat? format = null) => AppendText("\t", format);

    /// <summary>Appends a break.</summary>
    /// <param name="kind">What the break interrupts.</param>
    /// <param name="format">Character formatting of the run that carries it.</param>
    public Paragraph AppendBreak(BreakKind kind = BreakKind.Line, RunFormat? format = null) =>
        kind == BreakKind.Line
            ? AppendText("\n", format)
            : AppendObject(new Break { Kind = kind }, format);

    /// <summary>Appends a picture sized to the image's natural dimensions unless told otherwise.</summary>
    /// <param name="image">The image to show.</param>
    /// <param name="width">Rendered width, or <see langword="null"/> for the natural width.</param>
    /// <param name="height">Rendered height, or <see langword="null"/> for the natural height.</param>
    /// <param name="format">Character formatting of the run that carries it.</param>
    public Picture AppendPicture(ImageData image, Primitives.Length? width = null, Primitives.Length? height = null, RunFormat? format = null)
    {
        var picture = new Picture
        {
            Image = image,
            Width = width ?? image.NaturalWidth,
            Height = height ?? image.NaturalHeight,
            IsDirty = true,
        };

        AppendObject(picture, format);
        Document?.Media.Add(image);
        return picture;
    }

    /// <summary>Anchors a zero-width mark at the given offset.</summary>
    /// <param name="mark">The mark.</param>
    /// <param name="offset">Where it sits; defaults to the end of the paragraph.</param>
    public void AddMark(InlineMark mark, int? offset = null)
    {
        int position = Math.Clamp(offset ?? _length, 0, _length);
        _marks ??= [];
        int index = _marks.FindLastIndex(m => m.Offset <= position) + 1;
        _marks.Insert(index, new AnchoredMark { Offset = position, Mark = mark });
    }

    /// <summary>Wraps a stretch of the paragraph in a hyperlink, a revision or a content control.</summary>
    /// <param name="range">The wrapper.</param>
    /// <param name="start">Offset of the first covered character.</param>
    /// <param name="length">Number of characters covered.</param>
    public void AddRange(InlineRange range, int start, int length)
    {
        start = Math.Clamp(start, 0, _length);
        length = Math.Clamp(length, 0, _length - start);
        (_ranges ??= []).Add(new AnchoredRange { Start = start, Length = length, Range = range });
    }

    /// <summary>Removes a wrapper, leaving the text it covered in place.</summary>
    /// <param name="range">The wrapper to remove.</param>
    /// <returns><see langword="true"/> when it was found.</returns>
    public bool RemoveRange(InlineRange range) => _ranges?.RemoveAll(r => ReferenceEquals(r.Range, range)) > 0;

    /// <summary>Removes a mark.</summary>
    /// <param name="mark">The mark to remove.</param>
    /// <returns><see langword="true"/> when it was found.</returns>
    public bool RemoveMark(InlineMark mark) => _marks?.RemoveAll(m => ReferenceEquals(m.Mark, mark)) > 0;

    /// <summary>
    /// Tells an object that carries a container of its own where it now lives, so that
    /// container can find its way back to the document.
    /// </summary>
    internal void Anchor(InlineObject value)
    {
        if (value is Shape shape)
            shape.Host = this;
    }

    /// <summary>The object anchored at an offset, or <see langword="null"/> when the character is plain text.</summary>
    /// <param name="offset">Offset to look at.</param>
    public InlineObject? ObjectAt(int offset)
    {
        if (_objects is null)
            return null;

        foreach (AnchoredObject anchored in _objects)
        {
            if (anchored.Offset == offset)
                return anchored.Object;
            if (anchored.Offset > offset)
                break;
        }

        return null;
    }

    internal Span<RunSpan> RunSpans => CollectionsMarshal.AsSpan(_runs);

    internal List<AnchoredObject>? ObjectList => _objects;

    internal List<AnchoredMark>? MarkList => _marks;

    internal List<AnchoredRange>? RangeList => _ranges;

    /// <summary>Appends text as part of a run being read, keeping the run's own attributes.</summary>
    internal void AppendRunText(scoped ReadOnlySpan<char> text, RunFormat format, RunKind kind, string? attributes)
    {
        if (text.IsEmpty)
            return;

        int start = _length;
        AppendChars(text);
        ExtendOrAddRun(start, text.Length, format, kind, attributes);
    }

    /// <summary>Appends an object as part of a run being read, keeping the run's own attributes.</summary>
    internal void AppendRunObject(InlineObject value, RunFormat format, string? attributes)
    {
        int offset = _length;
        AppendChars([value.PlaceholderChar]);
        (_objects ??= []).Add(new AnchoredObject { Offset = offset, Object = value });
        Anchor(value);
        ExtendOrAddRun(offset, 1, format, RunKind.Text, attributes);
    }

    private void SetText(string value)
    {
        RunFormat? format = _runs.Count > 0 ? _runs[0].Format : null;
        Clear();
        AppendText(value, format);
    }

    private void AppendChars(scoped ReadOnlySpan<char> text)
    {
        EnsureCapacity(_length + text.Length);
        text.CopyTo(_buffer.AsSpan(_length));
        _length += text.Length;
        _cachedText = null;
    }

    private void EnsureCapacity(int needed)
    {
        if (_buffer.Length >= needed)
            return;
        Array.Resize(ref _buffer, Math.Max(needed, Math.Max(_buffer.Length * 2, 16)));
    }

    private void ExtendOrAddRun(int start, int length, RunFormat? format, RunKind kind, string? attributes)
    {
        format ??= _runs.Count > 0 ? _runs[^1].Format : RunFormat.Default;

        if (_runs.Count > 0)
        {
            ref RunSpan last = ref CollectionsMarshal.AsSpan(_runs)[^1];
            if (last.End == start && last.Kind == kind &&
                ReferenceEquals(last.Format, format) && last.Attributes == attributes)
            {
                last.Length += length;
                return;
            }
        }

        _runs.Add(new RunSpan { Start = start, Length = length, Format = format, Kind = kind, Attributes = attributes });
    }
}
