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
    private readonly Func<Paragraph, string?, string, int, bool> _appendNoteReference;
    private readonly Action<string, int> _reportRawHtml;
    private readonly DocumentLoadBudgetState _budget;
    private readonly CancellationToken _cancellationToken;
    private int _parseDepth;

    internal MarkdownInlineParser(
        IReadOnlyDictionary<string, (string Url, string? Title)> definitions,
        Func<string, string?, int, ImageData?> resolveImage,
        Func<Paragraph, string?, string, int, bool> appendNoteReference,
        Action<string, int> reportRawHtml,
        DocumentLoadBudgetState budget,
        CancellationToken cancellationToken)
    {
        _definitions = definitions;
        _resolveImage = resolveImage;
        _appendNoteReference = appendNoteReference;
        _reportRawHtml = reportRawHtml;
        _budget = budget;
        _cancellationToken = cancellationToken;
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

    private sealed class FootnoteNode(string raw, string? label) : Node
    {
        public string Raw { get; } = raw;

        public string? Label { get; } = label;
    }

    private sealed class RawHtmlNode(string text) : Node
    {
        public string Text { get; } = text;
    }

    private sealed class DelimNode(char kind, int count, bool canOpen, bool canClose) : Node
    {
        public char Kind { get; } = kind;

        public int OriginalCount { get; } = count;

        public int Count { get; set; } = count;

        public bool CanOpen { get; } = canOpen;

        public bool CanClose { get; } = canClose;
    }

    private readonly record struct OpenerEntry(LinkedListNode<Node> Item, long Order);

    private List<Node> ParseNodes(string text)
    {
        _parseDepth++;
        _budget.EnsureMarkupDepth(_parseDepth);
        try
        {
            var nodes = new List<Node>();
            var literal = new StringBuilder();
            HtmlTermini htmlTermini = ComputeHtmlTermini(text);
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
                if ((i & 1023) == 0)
                    _cancellationToken.ThrowIfCancellationRequested();
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

                    case '<' when RawHtml(text, i, htmlTermini) is { } html:
                        FlushLiteral();
                        AddNode(nodes, new RawHtmlNode(html.Text));
                        i = html.End;
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

                    case '[' when i + 1 < text.Length && text[i + 1] == '^':
                        {
                            (string raw, string? label, int end) = Footnote(text, i);
                            FlushLiteral();
                            AddNode(nodes, new FootnoteNode(raw, label));
                            i = end;
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

    private (string Text, string Url, int End)? Autolink(string text, int at)
    {
        int colon = -1;
        int atSign = -1;
        int dotAfterAt = -1;
        bool schemeCharacters = true;
        int end = at + 1;
        for (; end < text.Length && text[end] != '>'; end++)
        {
            if (((end - at) & 4095) == 0)
                _cancellationToken.ThrowIfCancellationRequested();

            char current = text[end];
            if (current is ' ' or '<')
                return null;

            int relative = end - at - 1;
            if (colon < 0)
            {
                if (current == ':')
                    colon = relative;
                else if (!char.IsAsciiLetterOrDigit(current) && current is not ('+' or '.' or '-'))
                    schemeCharacters = false;
            }

            if (atSign < 0 && current == '@')
                atSign = relative;
            else if (atSign >= 0 && dotAfterAt < 0 && current == '.')
                dotAfterAt = relative;
        }

        if (end >= text.Length || end == at + 1)
            return null;

        ReadOnlySpan<char> inner = text.AsSpan(at + 1, end - at - 1);
        if (colon > 0 && char.IsAsciiLetter(inner[0]) && schemeCharacters)
        {
            string value = inner.ToString();
            return (value, value, end + 1);
        }

        if (atSign > 0 && dotAfterAt > atSign)
        {
            string value = inner.ToString();
            return (value, "mailto:" + value, end + 1);
        }

        return null;
    }

    private static (string Raw, string? Label, int End) Footnote(string text, int at)
    {
        int close = text.IndexOf(']', at + 2);
        if (close < 0)
            return (text[at..], null, text.Length);

        string raw = text[at..(close + 1)];
        string label = text[(at + 2)..close];
        if (label.Length == 0 || label.Contains('\n'))
            return (raw, null, close + 1);

        return (raw, label, close + 1);
    }

    /// <summary>
    /// Recognises CommonMark-shaped inline HTML only far enough to keep it as literal text and
    /// issue a diagnostic. It intentionally does not interpret elements or attributes.
    /// </summary>
    private readonly record struct HtmlTermini(int Comment, int Processing, int CData, int Declaration);

    private HtmlTermini ComputeHtmlTermini(string text)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        int comment = -1;
        int processing = -1;
        int cdata = -1;
        int declaration = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0)
                _cancellationToken.ThrowIfCancellationRequested();
            if (text[i] != '>')
                continue;

            declaration = i;
            if (i >= 2 && text[i - 2] == '-' && text[i - 1] == '-')
                comment = i - 2;
            if (i >= 1 && text[i - 1] == '?')
                processing = i - 1;
            if (i >= 2 && text[i - 2] == ']' && text[i - 1] == ']')
                cdata = i - 2;
        }

        return new HtmlTermini(comment, processing, cdata, declaration);
    }

    private static (string Text, int End)? RawHtml(string text, int at, HtmlTermini termini)
    {
        if (text.AsSpan(at).StartsWith("<!--", StringComparison.Ordinal))
            return Delimited(text, at, "-->", termini.Comment);
        if (text.AsSpan(at).StartsWith("<?", StringComparison.Ordinal))
            return Delimited(text, at, "?>", termini.Processing);
        if (text.AsSpan(at).StartsWith("<![CDATA[", StringComparison.Ordinal))
            return Delimited(text, at, "]]>", termini.CData);

        if (text.AsSpan(at).StartsWith("<!", StringComparison.Ordinal) &&
            at + 2 < text.Length && char.IsAsciiLetter(text[at + 2]))
        {
            return Delimited(text, at, ">", termini.Declaration);
        }

        int i = at + 1;
        bool closing = i < text.Length && text[i] == '/';
        if (closing)
            i++;
        if (i >= text.Length || !char.IsAsciiLetter(text[i]))
            return null;

        i++;
        while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '-' or '_'))
            i++;

        if (closing)
        {
            while (i < text.Length && text[i] is ' ' or '\t' or '\n')
                i++;
            return i < text.Length && text[i] == '>' ? (text[at..(i + 1)], i + 1) : null;
        }

        while (i < text.Length)
        {
            while (i < text.Length && text[i] is ' ' or '\t' or '\n')
                i++;
            if (i >= text.Length)
                return null;
            if (text[i] == '>')
                return (text[at..(i + 1)], i + 1);
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '>')
                return (text[at..(i + 2)], i + 2);

            int nameStart = i;
            while (i < text.Length &&
                   text[i] is not (' ' or '\t' or '\n' or '=' or '/' or '>' or '<' or '"' or '\'' or '`'))
            {
                i++;
            }

            if (i == nameStart)
                return null;
            while (i < text.Length && text[i] is ' ' or '\t' or '\n')
                i++;
            if (i >= text.Length || text[i] != '=')
                continue;

            i++;
            while (i < text.Length && text[i] is ' ' or '\t' or '\n')
                i++;
            if (i >= text.Length)
                return null;
            if (text[i] is '"' or '\'')
            {
                char quote = text[i++];
                int close = text.IndexOf(quote, i);
                if (close < 0)
                    return null;
                i = close + 1;
                continue;
            }

            int valueStart = i;
            while (i < text.Length && text[i] is not (' ' or '\t' or '\n' or '"' or '\'' or '=' or '<' or '>' or '`'))
                i++;
            if (i == valueStart)
                return null;
        }

        return null;
    }

    private static (string Text, int End)? Delimited(
        string text, int at, string delimiter, int lastDelimiter)
    {
        // A failed search at one candidate proves every later candidate is unterminated too.
        // The precomputed last terminus keeps adversarial "<!--<!--..." input linear while
        // letting invalid HTML fall back to ordinary CommonMark inline parsing.
        if (lastDelimiter < at)
            return null;

        int end = text.IndexOf(delimiter, at + 2, StringComparison.Ordinal);
        return end < 0 ? null : (text[at..(end + delimiter.Length)], end + delimiter.Length);
    }

    private static (string Value, int End)? Entity(string text, int at)
    {
        int end = text.IndexOf(';', at + 1);
        if (end < 0 || end - at > 12)
            return null;

        string name = text[(at + 1)..end];
        if (name.StartsWith("#x", StringComparison.OrdinalIgnoreCase) &&
            name.Length is >= 3 and <= 8 &&
            int.TryParse(name.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
        {
            return (CodePoint(hex), end + 1);
        }

        if (name.StartsWith('#') &&
            name.Length is >= 2 and <= 8 &&
            int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int code))
        {
            return (CodePoint(code), end + 1);
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

    private static string CodePoint(int value) =>
        value is <= 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF)
            ? "\uFFFD"
            : char.ConvertFromUtf32(value);

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
    /// Pairs delimiter runs into emphasis, strong emphasis and strikethrough. A linked work list
    /// and one opener stack per delimiter kind keep both lookup and splicing amortized linear.
    /// </summary>
    private void ProcessEmphasis(List<Node> nodes, int depth = 1)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        _budget.EnsureMarkupDepth(depth);
        var work = new LinkedList<Node>(nodes);
        Stack<OpenerEntry>[] starOpeners = CreateOpenerBuckets();
        Stack<OpenerEntry>[] underscoreOpeners = CreateOpenerBuckets();
        Stack<OpenerEntry>[] strikeOpeners = CreateOpenerBuckets();
        int operations = 0;
        long openerOrder = 0;

        void CheckCancellation()
        {
            if ((++operations & 4095) == 0)
                _cancellationToken.ThrowIfCancellationRequested();
        }

        Stack<OpenerEntry>[] Openers(char kind) => kind switch
        {
            '*' => starOpeners,
            '_' => underscoreOpeners,
            _ => strikeOpeners,
        };

        (OpenerEntry Entry, int Bucket)? NearestAdmissibleOpener(
            DelimNode closer,
            Stack<OpenerEntry>[] buckets)
        {
            OpenerEntry? nearest = null;
            int nearestBucket = -1;
            for (int bucket = 0; bucket < buckets.Length; bucket++)
            {
                CheckCancellation();
                bool openerCanClose = bucket >= 3;
                int openerRemainder = bucket % 3;
                if (ViolatesRuleOfThree(closer, openerCanClose, openerRemainder))
                    continue;

                Stack<OpenerEntry> candidates = buckets[bucket];
                while (candidates.Count > 0 &&
                       (candidates.Peek().Item.List != work ||
                        (closer.Kind == '~' &&
                         ((DelimNode)candidates.Peek().Item.Value).Count < 2)))
                {
                    CheckCancellation();
                    candidates.Pop();
                }

                if (candidates.Count > 0 &&
                    (nearest is null || candidates.Peek().Order > nearest.Value.Order))
                {
                    nearest = candidates.Peek();
                    nearestBucket = bucket;
                }
            }

            return nearest is null ? null : (nearest.Value, nearestBucket);
        }

        for (LinkedListNode<Node>? item = work.First; item is not null;)
        {
            CheckCancellation();
            LinkedListNode<Node>? next = item.Next;
            if (item.Value is not DelimNode delimiter)
            {
                item = next;
                continue;
            }

            Stack<OpenerEntry>[] openers = Openers(delimiter.Kind);
            if (delimiter.CanClose)
            {
                while (item.List is not null && delimiter.Count > 0 &&
                       (delimiter.Kind != '~' || delimiter.Count >= 2))
                {
                    CheckCancellation();
                    if (NearestAdmissibleOpener(delimiter, openers) is not { } match)
                        break;

                    LinkedListNode<Node> openerItem = match.Entry.Item;
                    var opening = (DelimNode)openerItem.Value;
                    bool strike = delimiter.Kind == '~';
                    int used = strike ? 2 : Math.Min(Math.Min(opening.Count, delimiter.Count), 2);

                    var children = new List<Node>();
                    for (LinkedListNode<Node>? child = openerItem.Next; child != item;)
                    {
                        CheckCancellation();
                        LinkedListNode<Node> following = child!.Next!;
                        children.Add(child.Value);
                        work.Remove(child);
                        child = following;
                    }

                    _budget.AddMarkupNode();
                    var span = new SpanNode(children)
                    {
                        Bold = !strike && used == 2,
                        Italic = !strike && used == 1,
                        Strike = strike,
                    };
                    work.AddAfter(openerItem, span);

                    opening.Count -= used;
                    delimiter.Count -= used;
                    if (opening.Count <= 0)
                    {
                        openers[match.Bucket].Pop();
                        work.Remove(openerItem);
                    }

                    if (delimiter.Count <= 0)
                        work.Remove(item);
                }
            }

            if (item.List is not null && delimiter.Count > 0 && delimiter.CanOpen &&
                (delimiter.Kind != '~' || delimiter.Count >= 2))
            {
                int bucket = (delimiter.CanClose ? 3 : 0) + delimiter.OriginalCount % 3;
                openers[bucket].Push(new OpenerEntry(item, openerOrder++));
            }

            item = next;
        }

        // Whatever is left never paired and is the literal characters it always was.
        nodes.Clear();
        for (LinkedListNode<Node>? item = work.First; item is not null; item = item.Next)
        {
            CheckCancellation();
            Node node = item.Value;
            if (node is DelimNode leftover)
            {
                _budget.AddMarkupNode();
                node = new TextNode(new string(leftover.Kind, leftover.Count));
            }

            nodes.Add(node);
            if (node is SpanNode span)
                ProcessEmphasis(span.Children, depth + 1);
            else if (node is LinkNode link)
                ProcessEmphasis(link.Children, depth + 1);
        }
    }

    private static Stack<OpenerEntry>[] CreateOpenerBuckets() =>
        [new(), new(), new(), new(), new(), new()];

    /// <summary>
    /// CommonMark rules 9 and 10 forbid a match when either run can play both roles and the
    /// original run lengths form the rule-of-three combination. Bucketing openers by this
    /// immutable state lets the matcher skip an inadmissible nearest opener without rescanning.
    /// </summary>
    private static bool ViolatesRuleOfThree(
        DelimNode closer,
        bool openerCanClose,
        int openerRemainder)
    {
        if (closer.Kind is not ('*' or '_') || (!closer.CanOpen && !openerCanClose))
            return false;

        int closerRemainder = closer.OriginalCount % 3;
        return (openerRemainder + closerRemainder) % 3 == 0 &&
               (openerRemainder != 0 || closerRemainder != 0);
    }

    private void Emit(Paragraph paragraph, List<Node> nodes, RunFormat format, int line, int depth = 1)
    {
        _cancellationToken.ThrowIfCancellationRequested();
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

                case FootnoteNode footnote:
                    if (!_appendNoteReference(paragraph, footnote.Label, footnote.Raw, line))
                        paragraph.AppendText(footnote.Raw, format);
                    break;

                case RawHtmlNode html:
                    _reportRawHtml(html.Text, line);
                    paragraph.AppendText(html.Text, format);
                    break;

                default:
                    break;
            }
        }
    }

    private void Flatten(List<Node> nodes, StringBuilder plain, int depth = 1)
    {
        _cancellationToken.ThrowIfCancellationRequested();
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
                case FootnoteNode footnote:
                    plain.Append(footnote.Raw);
                    break;
                case RawHtmlNode html:
                    plain.Append(html.Text);
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
