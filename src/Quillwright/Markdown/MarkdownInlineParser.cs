using System.Globalization;
using System.Text;
using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Markdown;

/// <summary>
/// Turns one stretch of Markdown inline text into the runs and ranges of a paragraph: code
/// spans, links, images, emphasis by the CommonMark delimiter algorithm, entities and escapes.
/// </summary>
/// <remarks>
/// The paragraph is built through the model's own append calls, so a link is a real
/// <see cref="Hyperlink"/> range over real runs and an image is a real picture — nothing here
/// writes markup. Soft line endings arrive already folded to spaces and hard ones as
/// <c>\n</c>, which the model stores as a line break.
/// </remarks>
internal sealed class MarkdownInlineParser
{
    private readonly IReadOnlyDictionary<string, (string Url, string? Title)> _definitions;
    private readonly Func<string, string?, int, ImageData?> _resolveImage;
    private readonly DocumentLoadBudgetState _budget;
    private int _parseDepth;

    internal MarkdownInlineParser(
        IReadOnlyDictionary<string, (string Url, string? Title)> definitions,
        Func<string, string?, int, ImageData?> resolveImage,
        DocumentLoadBudgetState budget)
    {
        _definitions = definitions;
        _resolveImage = resolveImage;
        _budget = budget;
    }

    /// <summary>Parses inline text into a paragraph.</summary>
    /// <param name="paragraph">The paragraph to fill.</param>
    /// <param name="text">The inline source.</param>
    /// <param name="format">Formatting every run starts from.</param>
    /// <param name="line">The 1-based source line, for diagnostics.</param>
    public void Fill(Paragraph paragraph, string text, RunFormat format, int line)
    {
        List<Node> nodes = ParseNodes(text);
        ProcessEmphasis(nodes);
        Emit(paragraph, nodes, format, line);
    }

    /// <summary>The plain text of an inline source, for an image's alternative text.</summary>
    public string PlainText(string text)
    {
        List<Node> nodes = ParseNodes(text);
        ProcessEmphasis(nodes);
        var plain = new StringBuilder();
        Flatten(nodes, plain);
        return plain.ToString();
    }

    private abstract class Node
    {
    }

    private sealed class TextNode(string text) : Node
    {
        public string Text { get; set; } = text;
    }

    private sealed class BreakNode : Node
    {
    }

    private sealed class CodeNode(string text) : Node
    {
        public string Text { get; } = text;
    }

    private sealed class SpanNode(List<Node> children) : Node
    {
        public List<Node> Children { get; } = children;

        public bool Bold { get; init; }

        public bool Italic { get; init; }

        public bool Strike { get; init; }
    }

    private sealed class LinkNode(List<Node> children, string url, string? title) : Node
    {
        public List<Node> Children { get; } = children;

        public string Url { get; } = url;

        public string? Title { get; } = title;
    }

    private sealed class ImageNode(string alt, string url, string? title) : Node
    {
        public string Alt { get; } = alt;

        public string Url { get; } = url;

        public string? Title { get; } = title;
    }

    private sealed class DelimNode(char kind, int count, bool canOpen, bool canClose) : Node
    {
        public char Kind { get; } = kind;

        public int Count { get; set; } = count;

        public bool CanOpen { get; } = canOpen;

        public bool CanClose { get; } = canClose;
    }

    private List<Node> ParseNodes(string text)
    {
        _parseDepth++;
        _budget.EnsureMarkupDepth(_parseDepth);
        try
        {
            var nodes = new List<Node>();
            var literal = new StringBuilder();
            int i = 0;

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                AddNode(nodes, new TextNode(literal.ToString()));
                literal.Clear();
            }
        }

        while (i < text.Length)
        {
            char c = text[i];
            switch (c)
            {
                case '\\' when i + 1 < text.Length && IsAsciiPunctuation(text[i + 1]):
                    literal.Append(text[i + 1]);
                    i += 2;
                    continue;

                case '\n':
                    FlushLiteral();
                    AddNode(nodes, new BreakNode());
                    i++;
                    continue;

                case '`':
                {
                    int open = CountRun(text, i, '`');
                    int close = FindBacktickClose(text, i + open, open);
                    if (close < 0)
                    {
                        literal.Append(text, i, open);
                        i += open;
                        continue;
                    }

                    FlushLiteral();
                    AddNode(nodes, new CodeNode(CodeContent(text[(i + open)..close])));
                    i = close + open;
                    continue;
                }

                case '<' when Autolink(text, i) is { } autolink:
                    FlushLiteral();
                    _budget.AddMarkupNode(); // the link's text child
                    AddNode(nodes, new LinkNode([new TextNode(autolink.Text)], autolink.Url, null));
                    i = autolink.End;
                    continue;

                case '&' when Entity(text, i) is { } entity:
                    literal.Append(entity.Value);
                    i = entity.End;
                    continue;

                case '*' or '_':
                {
                    int count = CountRun(text, i, c);
                    (bool open, bool close) = Flanking(text, i, count, c);
                    FlushLiteral();
                    AddNode(nodes, new DelimNode(c, count, open, close));
                    i += count;
                    continue;
                }

                case '~' when CountRun(text, i, '~') >= 2:
                {
                    int count = CountRun(text, i, '~');
                    (bool open, bool close) = Flanking(text, i, count, '~');
                    FlushLiteral();
                    AddNode(nodes, new DelimNode('~', count, open, close));
                    i += count;
                    continue;
                }

                case '!' when i + 1 < text.Length && text[i + 1] == '[':
                case '[':
                {
                    bool image = c == '!';
                    int start = image ? i + 1 : i;
                    if (ParseBracket(text, start, image) is not { } parsed)
                    {
                        literal.Append(c);
                        i++;
                        continue;
                    }

                    FlushLiteral();
                    AddNode(nodes, parsed.Node);
                    i = parsed.End;
                    continue;
                }

                default:
                    literal.Append(c);
                    i++;
                    continue;
            }
        }

            FlushLiteral();
            return nodes;
        }
        finally
        {
            _parseDepth--;
        }
    }

    private void AddNode(List<Node> nodes, Node node)
    {
        _budget.AddMarkupNode();
        nodes.Add(node);
    }

    /// <summary>A complete bracket construct at <paramref name="open"/>, or nothing.</summary>
    private (Node Node, int End)? ParseBracket(string text, int open, bool image)
    {
        int close = MatchingBracket(text, open);
        if (close < 0)
            return null;

        string label = text[(open + 1)..close];
        int after = close + 1;

        // An inline link: the destination, and perhaps a title, in parentheses.
        if (after < text.Length && text[after] == '(')
        {
            if (ParseDestination(text, after) is not { } destination)
                return null;

            return (Make(label, destination.Url, destination.Title, image), destination.End);
        }

        // A reference: a second label, an empty pair, or nothing but the label itself.
        string reference = label;
        int end = after;
        if (after < text.Length && text[after] == '[')
        {
            int referenceClose = text.IndexOf(']', after + 1);
            if (referenceClose < 0)
                return null;

            string named = text[(after + 1)..referenceClose];
            if (named.Length > 0)
                reference = named;

            end = referenceClose + 1;
        }

        if (!_definitions.TryGetValue(NormalizeLabel(reference), out (string Url, string? Title) definition))
            return null;

        return (Make(label, definition.Url, definition.Title, image), end);
    }

    private Node Make(string label, string url, string? title, bool image)
    {
        if (image)
            return new ImageNode(PlainText(label), url, title);

        List<Node> children = ParseNodes(label);
        ProcessEmphasis(children);
        return new LinkNode(children, url, title);
    }

    /// <summary>The closing bracket that matches the one at <paramref name="open"/>.</summary>
    private static int MatchingBracket(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\')
            {
                i++;
            }
            else if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>Parses <c>(destination "title")</c> starting at the opening parenthesis.</summary>
    private static (string Url, string? Title, int End)? ParseDestination(string text, int open)
    {
        int i = open + 1;
        while (i < text.Length && text[i] is ' ' or '\n')
            i++;

        var url = new StringBuilder();
        if (i < text.Length && text[i] == '<')
        {
            i++;
            while (i < text.Length && text[i] != '>')
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                    i++;
                url.Append(text[i]);
                i++;
            }

            if (i >= text.Length)
                return null;

            i++;
        }
        else
        {
            int depth = 0;
            while (i < text.Length && text[i] is not (' ' or '\n'))
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    url.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '(')
                    depth++;
                if (c == ')')
                {
                    if (depth == 0)
                        break;
                    depth--;
                }

                url.Append(c);
                i++;
            }
        }

        while (i < text.Length && text[i] is ' ' or '\n')
            i++;

        string? title = null;
        if (i < text.Length && text[i] is '"' or '\'')
        {
            char quote = text[i];
            int titleEnd = text.IndexOf(quote, i + 1);
            if (titleEnd < 0)
                return null;

            title = text[(i + 1)..titleEnd];
            i = titleEnd + 1;
            while (i < text.Length && text[i] is ' ' or '\n')
                i++;
        }

        if (i >= text.Length || text[i] != ')')
            return null;

        return (url.ToString(), title, i + 1);
    }

    private static (string Text, string Url, int End)? Autolink(string text, int at)
    {
        int end = text.IndexOf('>', at + 1);
        if (end < 0)
            return null;

        string inner = text[(at + 1)..end];
        if (inner.Length == 0 || inner.Any(static c => c is ' ' or '<'))
            return null;

        int colon = inner.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0 && char.IsAsciiLetter(inner[0]) &&
            inner.AsSpan(0, colon).ToArray().All(static c => char.IsAsciiLetterOrDigit(c) || c is '+' or '.' or '-'))
        {
            return (inner, inner, end + 1);
        }

        int atSign = inner.IndexOf('@', StringComparison.Ordinal);
        if (atSign > 0 && inner.IndexOf('.', atSign) > atSign)
            return (inner, "mailto:" + inner, end + 1);

        return null;
    }

    private static (string Value, int End)? Entity(string text, int at)
    {
        int end = text.IndexOf(';', at + 1);
        if (end < 0 || end - at > 12)
            return null;

        string name = text[(at + 1)..end];
        if (name.StartsWith("#x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex) &&
            hex is > 0 and <= 0x10FFFF)
        {
            return (char.ConvertFromUtf32(hex), end + 1);
        }

        if (name.StartsWith('#') &&
            int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int code) &&
            code is > 0 and <= 0x10FFFF)
        {
            return (char.ConvertFromUtf32(code), end + 1);
        }

        return name switch
        {
            "amp" => ("&", end + 1),
            "lt" => ("<", end + 1),
            "gt" => (">", end + 1),
            "quot" => ("\"", end + 1),
            "apos" => ("'", end + 1),
            "nbsp" => (" ", end + 1),
            "mdash" => ("—", end + 1),
            "ndash" => ("–", end + 1),
            "hellip" => ("…", end + 1),
            "copy" => ("©", end + 1),
            _ => null,
        };
    }

    private static int CountRun(string text, int at, char c)
    {
        int count = 0;
        while (at + count < text.Length && text[at + count] == c)
            count++;

        return count;
    }

    private static int FindBacktickClose(string text, int from, int length)
    {
        int i = from;
        while (i < text.Length)
        {
            if (text[i] != '`')
            {
                i++;
                continue;
            }

            int run = CountRun(text, i, '`');
            if (run == length)
                return i;

            i += run;
        }

        return -1;
    }

    /// <summary>Line endings become spaces, and one space of padding comes off each end.</summary>
    private static string CodeContent(string content)
    {
        string flat = content.Replace('\n', ' ');
        if (flat.Length >= 2 && flat[0] == ' ' && flat[^1] == ' ' && flat.Trim().Length > 0)
            return flat[1..^1];

        return flat;
    }

    /// <summary>The flanking rules of CommonMark 6.2, which decide what a delimiter run may do.</summary>
    private static (bool CanOpen, bool CanClose) Flanking(string text, int at, int count, char kind)
    {
        char before = at > 0 ? text[at - 1] : '\n';
        char after = at + count < text.Length ? text[at + count] : '\n';

        bool left = !IsWhite(after) && (!IsPunct(after) || IsWhite(before) || IsPunct(before));
        bool right = !IsWhite(before) && (!IsPunct(before) || IsWhite(after) || IsPunct(after));

        if (kind != '_')
            return (left, right);

        return (left && (!right || IsPunct(before)), right && (!left || IsPunct(after)));

        static bool IsWhite(char c) => c is ' ' or '\n' or '\t';
        static bool IsPunct(char c) => IsAsciiPunctuation(c);
    }

    private static bool IsAsciiPunctuation(char c) =>
        c is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');

    /// <summary>
    /// Pairs delimiter runs into emphasis, strong emphasis and strikethrough, closers left to
    /// right against the nearest matching opener, shedding two markers a time while they last.
    /// </summary>
    private void ProcessEmphasis(List<Node> nodes, int depth = 1)
    {
        _budget.EnsureMarkupDepth(depth);
        for (int closer = 0; closer < nodes.Count; closer++)
        {
            if (nodes[closer] is not DelimNode closing || !closing.CanClose)
                continue;

            int opener = -1;
            for (int i = closer - 1; i >= 0; i--)
            {
                if (nodes[i] is DelimNode candidate && candidate.CanOpen && candidate.Kind == closing.Kind && candidate.Count > 0)
                {
                    opener = i;
                    break;
                }
            }

            if (opener < 0)
                continue;

            var opening = (DelimNode)nodes[opener];
            bool strike = closing.Kind == '~';
            int used = strike ? 2 : Math.Min(Math.Min(opening.Count, closing.Count), 2);

            var children = new List<Node>(nodes.GetRange(opener + 1, closer - opener - 1));
            _budget.AddMarkupNode();
            var span = new SpanNode(children)
            {
                Bold = !strike && used == 2,
                Italic = !strike && used == 1,
                Strike = strike,
            };

            nodes.RemoveRange(opener + 1, closer - opener - 1);
            nodes.Insert(opener + 1, span);

            opening.Count -= used;
            closing.Count -= used;
            if (opening.Count <= 0)
                nodes.RemoveAt(opener);

            int closingIndex = nodes.IndexOf(closing);
            if (closing.Count <= 0 && closingIndex >= 0)
                nodes.RemoveAt(closingIndex);

            // Something between the two may still pair; start the scan again from the span.
            closer = opener;
        }

        // Whatever is left never paired and is the literal characters it always was.
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is DelimNode leftover)
            {
                _budget.AddMarkupNode();
                nodes[i] = new TextNode(new string(leftover.Kind, leftover.Count));
            }
            else if (nodes[i] is SpanNode span)
                ProcessEmphasis(span.Children, depth + 1);
            else if (nodes[i] is LinkNode link)
                ProcessEmphasis(link.Children, depth + 1);
        }
    }

    private void Emit(Paragraph paragraph, List<Node> nodes, RunFormat format, int line, int depth = 1)
    {
        _budget.EnsureMarkupDepth(depth);
        foreach (Node node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    paragraph.AppendText(text.Text, format);
                    break;

                case BreakNode:
                    paragraph.AppendText("\n", format);
                    break;

                case CodeNode code:
                    paragraph.AppendText(code.Text, format with
                    {
                        FontAscii = "Consolas",
                        FontHighAnsi = "Consolas",
                    });
                    break;

                case SpanNode span:
                    Emit(paragraph, span.Children, format with
                    {
                        Bold = span.Bold ? true : format.Bold,
                        Italic = span.Italic ? true : format.Italic,
                        Strike = span.Strike ? true : format.Strike,
                    }, line, depth + 1);
                    break;

                case LinkNode link:
                {
                    int start = paragraph.TextLength;
                    Emit(paragraph, link.Children, format, line, depth + 1);
                    var hyperlink = new Hyperlink { Tooltip = link.Title };
                    if (link.Url.StartsWith('#'))
                        hyperlink.Anchor = link.Url[1..];
                    else
                        hyperlink.Url = link.Url;

                    paragraph.AddRange(hyperlink, start, paragraph.TextLength - start);
                    break;
                }

                case ImageNode image:
                {
                    ImageData? data = _resolveImage(image.Url, image.Alt, line);
                    if (data is not null)
                        paragraph.AppendPicture(data);
                    else if (image.Alt.Length > 0)
                        paragraph.AppendText(image.Alt, format);

                    break;
                }

                default:
                    break;
            }
        }
    }

    private void Flatten(List<Node> nodes, StringBuilder plain, int depth = 1)
    {
        _budget.EnsureMarkupDepth(depth);
        foreach (Node node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    plain.Append(text.Text);
                    break;
                case CodeNode code:
                    plain.Append(code.Text);
                    break;
                case BreakNode:
                    plain.Append(' ');
                    break;
                case SpanNode span:
                    Flatten(span.Children, plain, depth + 1);
                    break;
                case LinkNode link:
                    Flatten(link.Children, plain, depth + 1);
                    break;
                case ImageNode image:
                    plain.Append(image.Alt);
                    break;
                default:
                    break;
            }
        }
    }

    private static string NormalizeLabel(string label) =>
        string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLowerInvariant();

    /// <summary>Normalises a reference label the way lookups do.</summary>
    public static string LabelKey(string label) => NormalizeLabel(label);
}
