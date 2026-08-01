using Quillwright.Model;

namespace Quillwright.Doc;

/// <summary>
/// Turns <c>HYPERLINK</c> fields back into the links the model represents them with.
/// </summary>
/// <remarks>
/// The binary format has no link element. A link is a field: a begin character, an
/// instruction naming the target, a separator, the text the reader sees, and an end
/// character. Left as a field it would show as five pieces of content where the model
/// expects one range over some text, so the field is collapsed back into that range.
/// </remarks>
internal static class DocHyperlinkReader
{
    private const string Keyword = "HYPERLINK";

    /// <summary>Collapses every hyperlink field in a paragraph.</summary>
    /// <param name="paragraph">The paragraph, edited in place.</param>
    public static void Collapse(Paragraph paragraph)
    {
        // Each collapse shifts everything after it, so the paragraph is rescanned from the
        // start until no more fields are found.
        while (TryCollapseFirst(paragraph))
        {
        }
    }

    /// <summary>
    /// Finds one hyperlink field and collapses it. Fields nest — a table of contents entry
    /// is a hyperlink around a page reference — so the open ones are tracked on a stack and
    /// an end character closes the innermost.
    /// </summary>
    private static bool TryCollapseFirst(Paragraph paragraph)
    {
        var open = new Stack<(int Begin, int Separate)>();
        foreach ((int offset, InlineObject anchored) in paragraph.Objects.OrderBy(static o => o.Offset))
        {
            if (anchored is not FieldCharacter field)
                continue;

            switch (field.Kind)
            {
                case FieldCharKind.Begin:
                    open.Push((offset, -1));
                    break;
                case FieldCharKind.Separate when open.Count > 0:
                    open.Push((open.Pop().Begin, offset));
                    break;
                case FieldCharKind.End when open.Count > 0:
                    (int begin, int separate) = open.Pop();
                    if (separate > begin && Collapse(paragraph, begin, separate, offset))
                        return true;
                    break;
            }
        }

        return false;
    }

    private static bool Collapse(Paragraph paragraph, int begin, int separate, int end)
    {
        string instruction = paragraph.Text[(begin + 1)..separate];
        if (Parse(instruction) is not { } link)
            return false;

        int resultLength = end - separate - 1;
        paragraph.RemoveText(end, 1);
        paragraph.RemoveText(begin, separate - begin + 1);
        if (resultLength > 0)
            paragraph.AddRange(link, begin, resultLength);

        return true;
    }

    /// <summary>
    /// Reads the target out of a field instruction, or returns <see langword="null"/> when
    /// the field is not a hyperlink.
    /// </summary>
    private static Hyperlink? Parse(string instruction)
    {
        ReadOnlySpan<char> text = instruction.AsSpan().Trim();
        if (!text.StartsWith(Keyword, StringComparison.OrdinalIgnoreCase))
            return null;

        var link = new Hyperlink();
        text = text[Keyword.Length..];

        string? pending = null;
        foreach (string token in Tokens(text))
        {
            if (token is "\\l" or "\\o" or "\\t")
            {
                pending = token;
                continue;
            }

            if (token.StartsWith('\\'))
            {
                pending = null;
                continue;
            }

            switch (pending)
            {
                case "\\l":
                    link.Anchor = token;
                    break;
                case "\\o":
                    link.Tooltip = token;
                    break;
                case "\\t":
                    link.TargetFrame = token;
                    break;
                default:
                    link.Url ??= token;
                    break;
            }

            pending = null;
        }

        return link.Url is null && link.Anchor is null ? null : link;
    }

    /// <summary>Splits an instruction into its words, keeping quoted runs together.</summary>
    private static List<string> Tokens(ReadOnlySpan<char> text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= text.Length)
                break;

            if (text[i] == '"')
            {
                int close = text[(i + 1)..].IndexOf('"');
                if (close < 0)
                    break;
                tokens.Add(text.Slice(i + 1, close).ToString());
                i += close + 2;
                continue;
            }

            int space = text[i..].IndexOfAny(' ', '\t');
            int length = space < 0 ? text.Length - i : space;
            tokens.Add(text.Slice(i, length).ToString());
            i += length;
        }

        return tokens;
    }
}
