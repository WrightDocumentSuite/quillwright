using System.Net;
using System.Text;

namespace Quillwright.Markdown;

/// <summary>Context-specific escaping for generated Markdown and HTML.</summary>
internal static class MarkdownText
{
    public static string Escape(string text, bool tableCell = false)
    {
        var builder = new StringBuilder(text.Length + 8);
        bool lineStart = true;
        Append(builder, text.AsSpan(), tableCell, ref lineStart);
        return builder.ToString();
    }

    public static void Append(StringBuilder builder, ReadOnlySpan<char> text, bool tableCell, ref bool lineStart)
    {
        int orderedPunctuation = lineStart ? OrderedMarkerPunctuation(text) : -1;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\r' or '\n' or '\v')
            {
                if (tableCell)
                    builder.Append("<br>");
                else
                    builder.Append("  \n");
                lineStart = true;
                if (i + 1 < text.Length)
                {
                    int relative = OrderedMarkerPunctuation(text[(i + 1)..]);
                    orderedPunctuation = relative < 0 ? -1 : relative + i + 1;
                }
                else
                {
                    orderedPunctuation = -1;
                }
                continue;
            }

            bool escape = c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '~' or '&' ||
                          (tableCell && c == '|') ||
                          (c == '!' && i + 1 < text.Length && text[i + 1] == '[') ||
                          (lineStart && c is '#' or '>' or '+' or '-') ||
                          i == orderedPunctuation;

            if (escape)
                builder.Append('\\');
            builder.Append(c);

            if (!char.IsWhiteSpace(c))
                lineStart = false;
        }
    }

    public static string LinkDestination(string destination)
    {
        if (destination.Any(c => c is '\r' or '\n' or '\0' || char.IsControl(c)))
            throw new ArgumentException("A Markdown link destination cannot contain control characters.", nameof(destination));

        bool angled = destination.Any(c => char.IsWhiteSpace(c) || c is '(' or ')');
        var builder = new StringBuilder(destination.Length + 4);
        if (angled)
            builder.Append('<');

        foreach (char c in destination)
        {
            bool escape = c == '\\' ||
                          (angled && (c is '<' or '>')) ||
                          (!angled && (c is '(' or ')'));
            if (escape)
                builder.Append('\\');
            builder.Append(c);
        }

        if (angled)
            builder.Append('>');
        return builder.ToString();
    }

    public static string CodeSpan(string text)
    {
        int delimiterLength = Math.Max(1, LongestRun(text, '`') + 1);
        string delimiter = new('`', delimiterLength);
        bool pad = text.StartsWith('`') || text.EndsWith('`') ||
                   text.StartsWith(' ') && text.EndsWith(' ') && text.Trim(' ').Length > 0;
        return pad ? $"{delimiter} {text} {delimiter}" : $"{delimiter}{text}{delimiter}";
    }

    public static (char Character, int Length) Fence(IEnumerable<string> lines)
    {
        int backticks = 0;
        int tildes = 0;
        foreach (string line in lines)
        {
            backticks = Math.Max(backticks, LongestRun(line, '`'));
            tildes = Math.Max(tildes, LongestRun(line, '~'));
        }

        return backticks <= tildes
            ? ('`', Math.Max(3, backticks + 1))
            : ('~', Math.Max(3, tildes + 1));
    }

    public static string HtmlText(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    public static string HtmlAttribute(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private static int OrderedMarkerPunctuation(ReadOnlySpan<char> text)
    {
        int start = 0;
        while (start < text.Length && start < 3 && text[start] == ' ')
            start++;

        int digits = start;
        while (digits < text.Length && digits - start < 9 && char.IsAsciiDigit(text[digits]))
            digits++;

        return digits > start && digits < text.Length && text[digits] is '.' or ')' &&
               digits + 1 < text.Length && char.IsWhiteSpace(text[digits + 1])
            ? digits
            : -1;
    }

    private static int LongestRun(string text, char wanted)
    {
        int longest = 0;
        int current = 0;
        foreach (char c in text)
        {
            if (c == wanted)
            {
                current++;
                longest = Math.Max(longest, current);
            }
            else
            {
                current = 0;
            }
        }

        return longest;
    }
}
