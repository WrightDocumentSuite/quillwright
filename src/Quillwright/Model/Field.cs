using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>
/// A complex field inside a paragraph: the instruction Word evaluates and the cached result
/// it shows until the field is refreshed.
/// </summary>
/// <remarks>
/// WordprocessingML spells a field out as a sequence rather than an element — a begin
/// character, instruction runs, a separator, the cached result, an end character. This is a
/// view over that sequence, so the instruction can be read and the result replaced without
/// the caller reassembling the pieces.
/// </remarks>
public readonly struct Field
{
    private readonly SimpleField? _simple;
    private readonly int _resultStart;
    private readonly int _resultLength;

    internal Field(Paragraph paragraph, int begin, int separate, int end)
    {
        Paragraph = paragraph;
        BeginOffset = begin;
        SeparateOffset = separate;
        EndOffset = end;
        _resultStart = separate < 0 ? end : separate + 1;
        _resultLength = Math.Max(0, end - _resultStart);
    }

    internal Field(Paragraph paragraph, SimpleField simple, int start, int length)
    {
        Paragraph = paragraph;
        _simple = simple;
        BeginOffset = start;
        SeparateOffset = -1;
        EndOffset = start + length;
        _resultStart = start;
        _resultLength = length;
    }

    /// <summary>The paragraph the field lives in.</summary>
    public Paragraph Paragraph { get; }

    /// <summary>
    /// Whether the field is written as one element (<c>w:fldSimple</c>) rather than as the
    /// sequence of characters.
    /// </summary>
    public bool IsSimple => _simple is not null;

    /// <summary>Offset of the begin character, or of the result of a simple field.</summary>
    public int BeginOffset { get; }

    /// <summary>Offset of the separator, or <c>-1</c> when the field has none.</summary>
    public int SeparateOffset { get; }

    /// <summary>Offset one past the last character of the field.</summary>
    public int EndOffset { get; }

    /// <summary>Offset of the first character of the cached result.</summary>
    public int ResultStart => _resultStart;

    /// <summary>Number of characters in the cached result.</summary>
    public int ResultLength => _resultLength;

    /// <summary>Whether the field has somewhere to put a result at all.</summary>
    public bool HasResult => IsSimple || SeparateOffset >= 0;

    /// <summary>The instruction, for example <c>PAGE \* MERGEFORMAT</c>.</summary>
    public string Instruction
    {
        get
        {
            if (_simple is { } simple)
                return simple.Instruction;

            int instructionEnd = SeparateOffset < 0 ? EndOffset : SeparateOffset;
            return Paragraph.AsSpan()[(BeginOffset + 1)..instructionEnd].ToString().Trim();
        }
    }

    /// <summary>The name of the field, the first word of the instruction.</summary>
    public string Name
    {
        get
        {
            ReadOnlySpan<char> instruction = Instruction.AsSpan();
            int space = instruction.IndexOf(' ');
            return (space < 0 ? instruction : instruction[..space]).ToString().ToUpperInvariant();
        }
    }

    /// <summary>Whether the cached result is marked as out of date.</summary>
    public bool IsDirty
    {
        get => _simple is { } simple
            ? simple.Dirty
            : Paragraph.ObjectAt(BeginOffset) is FieldCharacter { Dirty: true };
        set
        {
            if (_simple is { } simple)
                simple.Dirty = value;
            else if (Paragraph.ObjectAt(BeginOffset) is FieldCharacter begin)
                begin.Dirty = value;
        }
    }

    /// <summary>The text currently shown for the field.</summary>
    public string Result => Paragraph.AsSpan().Slice(ResultStart, ResultLength).ToString();

    /// <summary>Replaces the cached result, keeping the instruction and the field structure.</summary>
    /// <param name="text">The new result text.</param>
    public void SetResult(string text)
    {
        if (!HasResult)
            throw new InvalidOperationException("The field has no separator, so it has no result to replace.");
        Paragraph.ReplaceText(ResultStart, ResultLength, text);
    }
}

/// <summary>Creating and finding fields.</summary>
public static class FieldExtensions
{
    /// <summary>
    /// Appends a complete field: the begin character, the instruction, the separator, the
    /// cached result and the end character.
    /// </summary>
    /// <param name="paragraph">Paragraph to append to.</param>
    /// <param name="instruction">The field instruction, for example <c>PAGE</c>.</param>
    /// <param name="result">Text shown until the field is refreshed.</param>
    /// <param name="format">Character formatting of the field.</param>
    public static Paragraph AppendField(this Paragraph paragraph, string instruction, string? result = null, RunFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentException.ThrowIfNullOrEmpty(instruction);

        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Begin, Dirty = result is null }, format);
        paragraph.AppendRunText(" " + instruction + " ", format ?? RunFormat.Default, RunKind.FieldInstruction, null);
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Separate }, format);
        if (!string.IsNullOrEmpty(result))
            paragraph.AppendText(result, format);
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.End }, format);
        return paragraph;
    }

    /// <summary>Appends a page-number field.</summary>
    /// <param name="paragraph">Paragraph to append to.</param>
    /// <param name="format">Character formatting of the field.</param>
    public static Paragraph AppendPageNumber(this Paragraph paragraph, RunFormat? format = null) =>
        paragraph.AppendField("PAGE", "1", format);

    /// <summary>Appends a total-page-count field.</summary>
    /// <param name="paragraph">Paragraph to append to.</param>
    /// <param name="format">Character formatting of the field.</param>
    public static Paragraph AppendPageCount(this Paragraph paragraph, RunFormat? format = null) =>
        paragraph.AppendField("NUMPAGES", "1", format);

    /// <summary>Appends a date field.</summary>
    /// <param name="paragraph">Paragraph to append to.</param>
    /// <param name="pattern">A Word date picture such as <c>dd.MM.yyyy</c>.</param>
    /// <param name="format">Character formatting of the field.</param>
    public static Paragraph AppendDate(this Paragraph paragraph, string pattern = "dd.MM.yyyy", RunFormat? format = null) =>
        paragraph.AppendField($"DATE \\@ \"{pattern}\"", DateTime.Now.ToString(pattern, System.Globalization.CultureInfo.CurrentCulture), format);

    /// <summary>
    /// Appends a table-of-contents field. It is written dirty, so the consumer builds the
    /// entries the first time the document is opened.
    /// </summary>
    /// <param name="paragraph">Paragraph to append to.</param>
    /// <param name="levels">Range of heading levels to include.</param>
    public static Paragraph AppendTableOfContents(this Paragraph paragraph, string levels = "1-3") =>
        paragraph.AppendField($"TOC \\o \"{levels}\" \\h \\z \\u", "Right-click and choose Update Field.");

    /// <summary>
    /// The complete fields in a paragraph, in the order they begin. Both of the forms the
    /// format allows come back the same way: the sequence of characters, and the single
    /// <c>w:fldSimple</c> element.
    /// </summary>
    /// <param name="paragraph">Paragraph to scan.</param>
    public static IEnumerable<Field> Fields(this Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var found = new List<Field>();
        var open = new Stack<(int Begin, int Separate)>();
        foreach ((int offset, InlineObject anchored) in paragraph.Objects)
        {
            if (anchored is not FieldCharacter character)
                continue;

            switch (character.Kind)
            {
                case FieldCharKind.Begin:
                    open.Push((offset, -1));
                    break;
                case FieldCharKind.Separate when open.Count > 0:
                    open.Push((open.Pop().Begin, offset));
                    break;
                case FieldCharKind.End when open.Count > 0:
                    (int begin, int separate) = open.Pop();
                    found.Add(new Field(paragraph, begin, separate, offset));
                    break;
            }
        }

        foreach ((int start, int length, InlineRange range) in paragraph.Ranges)
        {
            if (range is SimpleField simple)
                found.Add(new Field(paragraph, simple, start, length));
        }

        found.Sort(static (left, right) => left.BeginOffset.CompareTo(right.BeginOffset));
        return found;
    }

    /// <summary>Every field in the document, including headers, footers, notes and comments.</summary>
    /// <param name="document">Document to scan.</param>
    public static IEnumerable<Field> Fields(this WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.AllContainers
            .SelectMany(static container => container.Blocks.Paragraphs)
            .SelectMany(Fields);
    }
}
