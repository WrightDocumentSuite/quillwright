using System.Globalization;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Follows the field boundaries in a paragraph and says which characters actually print.
/// </summary>
/// <remarks>
/// A complex field is five things in a row — a begin mark, its instruction, a separator, the result
/// the producer cached, and an end mark — and only the result prints. Fields nest, so the state is
/// a stack; and a field the exporter computes itself, such as a page number, suppresses the cached
/// result so the two do not both appear.
/// </remarks>
internal sealed class FieldTracker
{
    private readonly Stack<State> _open = new();
    private readonly bool _compute;

    /// <summary>Creates a tracker.</summary>
    /// <param name="compute">
    /// Whether fields the exporter can compute are computed. When not, every cached result
    /// prints as Word cached it, which is what <c>UpdatePageFields</c> turned off asks for.
    /// </param>
    public FieldTracker(bool compute = true) => _compute = compute;

    /// <summary>Whether the characters at the current position print.</summary>
    public bool Prints => _open.Count == 0 || (_open.Peek().InResult && !_open.Peek().Suppressed);

    /// <summary>Notes a field boundary and reports the field that just became computable.</summary>
    /// <param name="boundary">The boundary met.</param>
    /// <returns>
    /// The field to print in place of the cached result, or <see langword="null"/> when the result
    /// itself should print.
    /// </returns>
    public PageField? Boundary(FieldCharKind boundary)
    {
        switch (boundary)
        {
            case FieldCharKind.Begin:
                _open.Push(new State());
                return null;

            case FieldCharKind.Separate when _open.Count > 0:
            {
                State state = _open.Peek();
                state.InResult = true;

                if (!_compute || Parse(state.Instruction.ToString()) is not { } field)
                    return null;

                state.Suppressed = true;
                return field;
            }

            case FieldCharKind.End when _open.Count > 0:
                _open.Pop();
                return null;

            default:
                return null;
        }
    }

    /// <summary>Takes in a piece of a field instruction, which never prints.</summary>
    /// <param name="text">The instruction text.</param>
    public void Instruction(ReadOnlySpan<char> text)
    {
        if (_open.Count > 0)
            _open.Peek().Instruction.Append(text);
    }

    /// <summary>
    /// Reads a field instruction and says what the exporter should compute, or
    /// <see langword="null"/> when the cached result is the best answer available.
    /// </summary>
    /// <param name="instruction">The instruction, for example <c>PAGE \* roman</c>.</param>
    public static PageField? Parse(string? instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction))
            return null;

        ReadOnlySpan<char> text = instruction.AsSpan().Trim();
        int space = text.IndexOf(' ');
        ReadOnlySpan<char> name = space < 0 ? text : text[..space];

        PageFieldKind kind;
        string? bookmark = null;
        ReadOnlySpan<char> switches = space < 0 ? default : text[space..];

        if (name.Equals("PAGE", StringComparison.OrdinalIgnoreCase))
        {
            kind = PageFieldKind.Page;
        }
        else if (name.Equals("NUMPAGES", StringComparison.OrdinalIgnoreCase))
        {
            kind = PageFieldKind.NumPages;
        }
        else if (name.Equals("SECTIONPAGES", StringComparison.OrdinalIgnoreCase))
        {
            kind = PageFieldKind.SectionPages;
        }
        else if (name.Equals("PAGEREF", StringComparison.OrdinalIgnoreCase))
        {
            // The bookmark is the first thing after the keyword; what follows it is switches.
            // \p asks for "above" or "below" rather than a number, which only Word's own
            // update writes, so the cached result stays the best answer for it.
            ReadOnlySpan<char> rest = switches.TrimStart();
            int end = rest.IndexOf(' ');
            ReadOnlySpan<char> target = end < 0 ? rest : rest[..end];
            if (target.IsEmpty || target[0] == '\\')
                return null;

            switches = end < 0 ? default : rest[end..];
            if (switches.Contains("\\p", StringComparison.OrdinalIgnoreCase))
                return null;

            kind = PageFieldKind.PageRef;
            bookmark = target.ToString();
        }
        else
        {
            return null;
        }

        bool stated = switches.Contains("\\*", StringComparison.Ordinal);
        return new PageField(kind, FormatSwitch(switches), stated, bookmark);
    }

    /// <summary>
    /// The numbering scheme named by a general formatting switch (<c>\*</c>). The case of the
    /// keyword is the value, not decoration: <c>roman</c> counts in lower case and <c>ROMAN</c> in
    /// upper. Anything the switch does not name prints as digits.
    /// </summary>
    private static ListNumberFormat FormatSwitch(ReadOnlySpan<char> switches)
    {
        int marker = switches.IndexOf("\\*", StringComparison.Ordinal);
        if (marker < 0)
            return ListNumberFormat.Decimal;

        ReadOnlySpan<char> rest = switches[(marker + 2)..].TrimStart();
        int end = rest.IndexOf(' ');
        string word = (end < 0 ? rest : rest[..end]).ToString();

        return word switch
        {
            "roman" => ListNumberFormat.LowerRoman,
            "ROMAN" or "Roman" => ListNumberFormat.UpperRoman,
            "alphabetic" => ListNumberFormat.LowerLetter,
            "ALPHABETIC" or "Alphabetic" => ListNumberFormat.UpperLetter,
            _ => word.ToLower(CultureInfo.InvariantCulture) switch
            {
                "ordinal" => ListNumberFormat.Ordinal,
                "cardtext" => ListNumberFormat.CardinalText,
                "ordtext" => ListNumberFormat.OrdinalText,
                _ => ListNumberFormat.Decimal,
            },
        };
    }

    /// <summary>A field the exporter computes itself.</summary>
    /// <param name="Kind">Which quantity it prints.</param>
    /// <param name="Format">The numbering scheme to print it in.</param>
    /// <param name="FormatStated">
    /// Whether the instruction named that scheme. When it did not, the section's own page
    /// numbering decides, which is how a preface comes out in roman numerals without every field
    /// in it having to say so.
    /// </param>
    /// <param name="Bookmark">The bookmark a <c>PAGEREF</c> points at; nothing for the other kinds.</param>
    public readonly record struct PageField(
        PageFieldKind Kind, ListNumberFormat Format, bool FormatStated, string? Bookmark = null);

    private sealed class State
    {
        public System.Text.StringBuilder Instruction { get; } = new();

        public bool InResult { get; set; }

        public bool Suppressed { get; set; }
    }
}
