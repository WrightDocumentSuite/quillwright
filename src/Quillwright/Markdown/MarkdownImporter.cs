using System.Text;
using Quillwright.Diagnostics;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Markdown;

/// <summary>
/// Turns Markdown into a document: CommonMark's blocks and inlines, and the GitHub extensions
/// a table, a strikethrough and a task list come from, mapped onto the same styles the
/// exporter reads back — so <c>Import</c> and <c>ToMarkdown</c> are inverses as far as the two
/// formats can be.
/// </summary>
/// <remarks>
/// Headings become <c>Heading1</c>–<c>Heading6</c>, quotes <c>Quote</c>, code blocks
/// <c>CodeBlock</c> over Consolas, lists real numbering instances, tables real tables with a
/// repeating header row, links real hyperlink ranges and images real pictures. What has no
/// Word counterpart — raw HTML, a front-matter block — is carried as text or skipped, and
/// every such decision is named in the diagnostics with its line.
/// </remarks>
public static class MarkdownImporter
{
    /// <summary>Imports Markdown into a new document.</summary>
    /// <param name="markdown">The Markdown source.</param>
    /// <param name="options">How to import it, or <see langword="null"/> for the defaults.</param>
    public static MarkdownImportResult Import(string markdown, MarkdownImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var context = new ImportContext(options ?? new MarkdownImportOptions());
        context.Budget.ValidateText(markdown);
        List<Line> lines = Split(markdown);
        SkipFrontMatter(lines, context);
        CollectDefinitions(lines, context);

        context.Parser = new MarkdownInlineParser(context.Definitions, context.ResolveImage, context.Budget);
        ParseBlocks(
            lines, 0, lines.Count, context, context.Document.Sections[0].Blocks,
            quoted: false, listDepth: -1, markupDepth: 1);

        if (context.Document.Sections[0].Blocks.Count == 0)
            context.Document.Sections[0].AddParagraph(string.Empty);

        return new MarkdownImportResult(context.Document, context.Diagnostics);
    }

    /// <summary>Reads a Markdown file and imports it, resolving images beside the file.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="options">
    /// How to import it; when no media directory is set, the file's own directory is used.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<MarkdownImportResult> ImportFileAsync(
        string path, MarkdownImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        options ??= new MarkdownImportOptions();
        string markdown = await DocumentInput.ReadUtf8TextFileAsync(path, options.Budget, cancellationToken).ConfigureAwait(false);
        if (options.MediaDirectory is null)
            options = options with { MediaDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) };

        return Import(markdown, options);
    }

    private sealed class ImportContext
    {
        public ImportContext(MarkdownImportOptions options)
        {
            Options = options;
            Budget = new DocumentLoadBudgetState(options.Budget);
        }

        public MarkdownImportOptions Options { get; }

        public DocumentLoadBudgetState Budget { get; }

        public WordDocument Document { get; } = WordDocument.Create();

        public MarkdownImportDiagnostics Diagnostics { get; } = new();

        public Dictionary<string, (string Url, string? Title)> Definitions { get; } = [];

        public MarkdownInlineParser Parser { get; set; } = null!;

        public int BulletList { get; set; } = -1;

        public int OrderedList { get; set; } = -1;

        public ImageData? ResolveImage(string url, string? alt, int line)
        {
            if (!Options.ImportImages)
                return null;

            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return DataUri(url, line);

            if (url.Contains("://", StringComparison.Ordinal))
            {
                Diagnostics.Add(
                    MarkdownImportWarningKind.ImageSkipped,
                    "A remote image is not fetched — nothing here opens a network connection — so its alternative text stands in for it",
                    url, line);
                return null;
            }

            if (Options.MediaDirectory is null)
            {
                Diagnostics.Add(
                    MarkdownImportWarningKind.ImageSkipped,
                    "No media directory was given, so a relative image path cannot be resolved",
                    url, line);
                return null;
            }

            if (Budget.MaximumNextMediaBytes < 1)
            {
                throw new DocumentLoadLimitException(
                    nameof(DocumentLoadBudget.MaxTotalMediaBytes),
                    Budget.Budget.MaxTotalMediaBytes,
                    Budget.Budget.MaxTotalMediaBytes + 1);
            }

            MediaFileReadResult file = MediaFileResolver.Read(
                Options.MediaDirectory, url, Budget.MaximumNextMediaBytes);
            if (file.Status == MediaFileReadStatus.Unsafe)
            {
                Diagnostics.Add(
                    MarkdownImportWarningKind.ImageSkipped,
                    "A rooted image path, a traversal segment or a symbolic link is not followed",
                    url, line);
                return null;
            }

            if (file.Status == MediaFileReadStatus.Missing)
            {
                Diagnostics.Add(MarkdownImportWarningKind.ImageSkipped, "The image file does not exist", url, line);
                return null;
            }

            if (file.Status == MediaFileReadStatus.Unreadable)
            {
                Diagnostics.Add(MarkdownImportWarningKind.ImageSkipped, "The image file could not be read", url, line);
                return null;
            }

            if (file.Status == MediaFileReadStatus.TooLarge)
            {
                Budget.EnsureMedia(file.Length);
                Diagnostics.Add(MarkdownImportWarningKind.ImageSkipped, "The image file is too large to read", url, line);
                return null;
            }

            Budget.AddMedia(file.Bytes!.LongLength);
            return Document.Media.Add(ImageData.FromBytes(file.Bytes!));
        }

        private ImageData? DataUri(string url, int line)
        {
            int comma = url.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0 || !url.AsSpan(0, comma).Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                Diagnostics.Add(MarkdownImportWarningKind.ImageSkipped, "Only a base64 data URI can be decoded", null, line);
                return null;
            }

            try
            {
                ReadOnlySpan<char> encoded = url.AsSpan(comma + 1);
                Budget.EnsureMedia(EstimatedBase64Bytes(encoded));
                byte[] bytes = Convert.FromBase64String(url[(comma + 1)..]);
                Budget.AddMedia(bytes.LongLength);
                return Document.Media.Add(ImageData.FromBytes(bytes));
            }
            catch (FormatException)
            {
                Diagnostics.Add(MarkdownImportWarningKind.ImageSkipped, "The data URI is not valid base64", null, line);
                return null;
            }
        }

        private static long EstimatedBase64Bytes(ReadOnlySpan<char> encoded)
        {
            long characters = 0;
            int padding = 0;
            foreach (char character in encoded)
            {
                if (char.IsWhiteSpace(character))
                    continue;
                characters++;
                padding = character == '=' ? padding + 1 : 0;
            }

            long bytes = ((characters + 3) / 4) * 3 - Math.Min(padding, 2);
            return Math.Max(0, bytes);
        }
    }

    /// <summary>One source line, with the 1-based number it had before anything was stripped.</summary>
    private readonly record struct Line(string Text, int Number)
    {
        public Line Strip(int columns) => new(Text.Length <= columns ? string.Empty : Text[columns..], Number);
    }

    private static List<Line> Split(string markdown)
    {
        var lines = new List<Line>();
        int number = 1;
        foreach (string raw in markdown.Split('\n'))
        {
            lines.Add(new Line(raw.TrimEnd('\r').Replace("\t", "    ", StringComparison.Ordinal), number));
            number++;
        }

        return lines;
    }

    private static void SkipFrontMatter(List<Line> lines, ImportContext context)
    {
        if (lines.Count == 0 || lines[0].Text != "---")
            return;

        for (int i = 1; i < lines.Count; i++)
        {
            if (lines[i].Text is "---" or "...")
            {
                context.Diagnostics.Add(
                    MarkdownImportWarningKind.UnsupportedSyntax,
                    "A front-matter block is metadata for another tool and was skipped",
                    null, lines[0].Number);
                lines.RemoveRange(0, i + 1);
                return;
            }
        }
    }

    /// <summary>
    /// Link reference definitions, collected up front and removed, because a reference may sit
    /// before the definition it uses.
    /// </summary>
    private static void CollectDefinitions(List<Line> lines, ImportContext context)
    {
        bool fenced = false;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string text = lines[i].Text;
            if (Fence(text) is not null)
                fenced = !fenced;

            if (fenced)
                continue;

            string trimmed = text.TrimStart();
            if (text.Length - trimmed.Length > 3 || !trimmed.StartsWith('['))
                continue;

            int close = trimmed.IndexOf("]:", StringComparison.Ordinal);
            if (close <= 1)
                continue;

            string label = trimmed[1..close];
            string rest = trimmed[(close + 2)..].Trim();
            if (rest.Length == 0)
                continue;

            string url;
            string? title = null;
            int space = rest.IndexOf(' ', StringComparison.Ordinal);
            if (space < 0)
            {
                url = rest;
            }
            else
            {
                url = rest[..space];
                string remainder = rest[space..].Trim();
                if (remainder.Length >= 2 && (remainder[0] is '"' or '\'') && remainder[^1] == remainder[0])
                    title = remainder[1..^1];
                else
                    continue;
            }

            context.Budget.AddMarkupNode();
            context.Definitions[MarkdownInlineParser.LabelKey(label)] = (url.Trim('<', '>'), title);
            lines.RemoveAt(i);
        }
    }

    private static void ParseBlocks(
        List<Line> lines,
        int from,
        int to,
        ImportContext context,
        IList<Block> target,
        bool quoted,
        int listDepth,
        int markupDepth)
    {
        context.Budget.EnsureMarkupDepth(markupDepth);
        int i = from;
        var paragraph = new List<Line>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
                return;

            AddParagraph(paragraph, context, target, quoted);
            paragraph.Clear();
        }

        while (i < to)
        {
            Line line = lines[i];
            string text = line.Text;
            string trimmed = text.TrimStart();
            int indent = text.Length - trimmed.Length;

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                i++;
                continue;
            }

            // A setext underline turns the open paragraph into a heading.
            if (paragraph.Count > 0 && indent < 4 && IsSetext(trimmed, out int setextLevel))
            {
                AddHeading(paragraph, setextLevel, context, target);
                paragraph.Clear();
                i++;
                continue;
            }

            if (paragraph.Count == 0 && indent < 4 && IsThematicBreak(trimmed))
            {
                AddBlock(context, target, Rule());
                i++;
                continue;
            }

            if (indent < 4 && AtxHeading(trimmed) is { } atx)
            {
                FlushParagraph();
                AddHeading([line with { Text = atx.Text }], atx.Level, context, target);
                i++;
                continue;
            }

            if (indent < 4 && Fence(trimmed) is { } fence)
            {
                FlushParagraph();
                i = AddFencedCode(lines, i, to, fence, indent, context, target);
                continue;
            }

            if (indent >= 4 && paragraph.Count == 0)
            {
                FlushParagraph();
                i = AddIndentedCode(lines, i, to, context, target);
                continue;
            }

            if (indent < 4 && trimmed.StartsWith('>'))
            {
                FlushParagraph();
                i = AddQuote(lines, i, to, context, target, listDepth, markupDepth);
                continue;
            }

            if (indent < 4 && ListMarker(text) is { } marker && !(paragraph.Count > 0 && marker.Ordered && marker.Number != 1))
            {
                FlushParagraph();
                i = AddList(lines, i, to, marker, context, target, quoted, listDepth + 1, markupDepth);
                continue;
            }

            if (paragraph.Count == 0 && text.Contains('|', StringComparison.Ordinal) &&
                i + 1 < to && IsTableDelimiter(lines[i + 1].Text))
            {
                i = AddTable(lines, i, to, context, target);
                continue;
            }

            paragraph.Add(line);
            i++;
        }

        FlushParagraph();
    }

    private static void AddParagraph(List<Line> source, ImportContext context, IList<Block> target, bool quoted)
    {
        string inline = JoinParagraph(source);
        var paragraph = new Paragraph();
        if (quoted)
        {
            paragraph.Format = paragraph.Format with { StyleId = context.Document.Styles.GetOrAdd("Quote").Id };
        }

        if (inline.TrimStart().StartsWith('<'))
        {
            context.Diagnostics.Add(
                MarkdownImportWarningKind.HtmlKeptAsText,
                "Raw HTML has no interpreter here and was kept as the text it is",
                Snippet(inline), source[0].Number);
        }

        context.Parser.Fill(paragraph, inline, RunFormat.Default, source[0].Number);
        AddBlock(context, target, paragraph);
    }

    private static void AddHeading(List<Line> source, int level, ImportContext context, IList<Block> target)
    {
        var paragraph = new Paragraph();
        paragraph.Format = paragraph.Format with { StyleId = context.Document.Styles.GetOrAdd($"Heading{level}").Id };
        context.Parser.Fill(paragraph, JoinParagraph(source), RunFormat.Default, source[0].Number);
        AddBlock(context, target, paragraph);
    }

    /// <summary>Soft endings fold to spaces; two trailing spaces or a backslash break the line.</summary>
    private static string JoinParagraph(List<Line> source)
    {
        var joined = new StringBuilder();
        for (int i = 0; i < source.Count; i++)
        {
            string text = source[i].Text.TrimStart();
            if (i == source.Count - 1)
            {
                joined.Append(text.TrimEnd());
                break;
            }

            if (text.EndsWith("  ", StringComparison.Ordinal))
            {
                joined.Append(text.TrimEnd()).Append('\n');
            }
            else if (text.EndsWith('\\'))
            {
                joined.Append(text, 0, text.Length - 1).Append('\n');
            }
            else
            {
                joined.Append(text.TrimEnd()).Append(' ');
            }
        }

        return joined.ToString();
    }

    private static Paragraph Rule() => new()
    {
        Format = ParagraphFormat.Default with
        {
            Borders = BorderSet.Empty with
            {
                Bottom = BorderLine.Single(Length.FromPoints(0.75), WordColor.Auto),
            },
        },
    };

    private static (int Level, string Text)? AtxHeading(string trimmed)
    {
        int level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
            level++;

        if (level is 0 or > 6 || (level < trimmed.Length && trimmed[level] is not (' ' or '\t')))
            return level is >= 1 and <= 6 && level == trimmed.Length ? (level, string.Empty) : null;

        string text = trimmed[level..].Trim();

        // A run of closing hashes is decoration, not content.
        int end = text.Length;
        while (end > 0 && text[end - 1] == '#')
            end--;
        if (end < text.Length && (end == 0 || text[end - 1] is ' ' or '\t'))
            text = text[..end].TrimEnd();

        return (level, text);
    }

    private static bool IsSetext(string trimmed, out int level)
    {
        level = 0;
        string body = trimmed.TrimEnd();
        if (body.Length == 0)
            return false;

        if (body.All(static c => c == '='))
        {
            level = 1;
            return true;
        }

        if (body.All(static c => c == '-'))
        {
            level = 2;
            return true;
        }

        return false;
    }

    private static bool IsThematicBreak(string trimmed)
    {
        char kind = trimmed[0];
        if (kind is not ('-' or '*' or '_'))
            return false;

        int count = 0;
        foreach (char c in trimmed)
        {
            if (c == kind)
                count++;
            else if (c is not (' ' or '\t'))
                return false;
        }

        return count >= 3;
    }

    private static (char Kind, int Length)? Fence(string trimmed)
    {
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
            return ('`', CountLeading(trimmed, '`'));
        if (trimmed.StartsWith("~~~", StringComparison.Ordinal))
            return ('~', CountLeading(trimmed, '~'));
        return null;
    }

    private static int CountLeading(string text, char c)
    {
        int count = 0;
        while (count < text.Length && text[count] == c)
            count++;
        return count;
    }

    private static int AddFencedCode(
        List<Line> lines, int at, int to, (char Kind, int Length) fence, int indent, ImportContext context, IList<Block> target)
    {
        var code = new StringBuilder();
        int i = at + 1;
        for (; i < to; i++)
        {
            string text = lines[i].Text;
            string trimmed = text.TrimStart();
            if (Fence(trimmed) is { Kind: var closeKind, Length: var closeLength } && closeKind == fence.Kind &&
                closeLength >= fence.Length && trimmed.TrimEnd().All(c => c == fence.Kind))
            {
                i++;
                break;
            }

            code.Append(text.Length >= indent ? text[Math.Min(indent, text.Length - text.TrimStart().Length)..] : text).Append('\n');
        }

        AddBlock(context, target, CodeParagraph(code.ToString().TrimEnd('\n'), context));
        return i;
    }

    private static int AddIndentedCode(List<Line> lines, int at, int to, ImportContext context, IList<Block> target)
    {
        var code = new StringBuilder();
        int i = at;
        while (i < to)
        {
            string text = lines[i].Text;
            if (text.TrimEnd().Length == 0)
            {
                // A blank line continues the block only if more indented code follows.
                int ahead = i;
                while (ahead < to && lines[ahead].Text.TrimEnd().Length == 0)
                    ahead++;
                if (ahead >= to || lines[ahead].Text.Length - lines[ahead].Text.TrimStart().Length < 4)
                    break;

                for (; i < ahead; i++)
                    code.Append('\n');
                continue;
            }

            if (text.Length - text.TrimStart().Length < 4)
                break;

            code.Append(text[4..]).Append('\n');
            i++;
        }

        AddBlock(context, target, CodeParagraph(code.ToString().TrimEnd('\n'), context));
        return i;
    }

    private static Paragraph CodeParagraph(string code, ImportContext context)
    {
        Style style = context.Document.Styles.GetOrAdd("CodeBlock");
        var paragraph = new Paragraph { Format = ParagraphFormat.Default with { StyleId = style.Id } };
        paragraph.AppendText(code, RunFormat.Default with { FontAscii = "Consolas", FontHighAnsi = "Consolas" });
        return paragraph;
    }

    private static int AddQuote(
        List<Line> lines,
        int at,
        int to,
        ImportContext context,
        IList<Block> target,
        int listDepth,
        int markupDepth)
    {
        var inner = new List<Line>();
        int i = at;
        while (i < to)
        {
            string text = lines[i].Text;
            string trimmed = text.TrimStart();
            if (!trimmed.StartsWith('>'))
                break;

            string stripped = trimmed[1..];
            if (stripped.StartsWith(' '))
                stripped = stripped[1..];

            inner.Add(lines[i] with { Text = stripped });
            i++;
        }

        ParseBlocks(inner, 0, inner.Count, context, target, quoted: true, listDepth, markupDepth + 1);
        return i;
    }

    private readonly record struct Marker(bool Ordered, int Number, int ContentIndent, bool Task, bool TaskDone, string First);

    private static Marker? ListMarker(string line)
    {
        string trimmed = line.TrimStart();
        int indent = line.Length - trimmed.Length;
        if (indent > 3 || trimmed.Length == 0)
            return null;
        bool ordered = false;
        int number = 1;
        int markerLength;

        if (trimmed.Length >= 2 && trimmed[0] is '-' or '*' or '+' && trimmed[1] is ' ' or '\t')
        {
            markerLength = 1;
        }
        else if (char.IsAsciiDigit(trimmed[0]))
        {
            int digits = 0;
            while (digits < trimmed.Length && digits < 9 && char.IsAsciiDigit(trimmed[digits]))
                digits++;

            if (digits == 0 || digits >= trimmed.Length || trimmed[digits] is not ('.' or ')') ||
                digits + 1 >= trimmed.Length || trimmed[digits + 1] is not (' ' or '\t'))
            {
                return null;
            }

            ordered = true;
            number = int.Parse(trimmed.AsSpan(0, digits), System.Globalization.CultureInfo.InvariantCulture);
            markerLength = digits + 1;
        }
        else
        {
            return null;
        }

        int contentStart = markerLength;
        while (contentStart < trimmed.Length && trimmed[contentStart] is ' ' or '\t')
            contentStart++;

        string first = trimmed[contentStart..];
        bool task = false;
        bool done = false;
        if (first.Length >= 3 && first[0] == '[' && first[2] == ']' && (first.Length == 3 || first[3] is ' ' or '\t'))
        {
            if (first[1] is ' ')
                task = true;
            else if (first[1] is 'x' or 'X')
            {
                task = true;
                done = true;
            }

            if (task)
                first = first.Length > 3 ? first[4..].TrimStart() : string.Empty;
        }

        return new Marker(ordered, number, indent + contentStart, task, done, first);
    }

    private static int AddList(
        List<Line> lines,
        int at,
        int to,
        Marker first,
        ImportContext context,
        IList<Block> target,
        bool quoted,
        int depth,
        int markupDepth)
    {
        if (depth == 0)
        {
            context.BulletList = -1;
            context.OrderedList = -1;
        }

        int instance = first.Ordered
            ? context.OrderedList >= 0 ? context.OrderedList : context.OrderedList = context.Document.Numbering.AddNumberedList()
            : context.BulletList >= 0 ? context.BulletList : context.BulletList = context.Document.Numbering.AddBulletList();

        int i = at;
        while (i < to)
        {
            Marker? marker = ListMarker(lines[i].Text);
            if (marker is null || marker.Value.Ordered != first.Ordered)
                break;

            var item = new List<Line> { lines[i] with { Text = marker.Value.Task ? marker.Value.First : lines[i].Text.TrimStart()[(marker.Value.ContentIndent - (lines[i].Text.Length - lines[i].Text.TrimStart().Length))..] } };
            int content = marker.Value.ContentIndent;
            i++;

            while (i < to)
            {
                string text = lines[i].Text;
                if (text.TrimEnd().Length == 0)
                {
                    // The blank belongs to the item only when indented content follows it.
                    if (i + 1 < to && lines[i + 1].Text.TrimEnd().Length > 0 &&
                        lines[i + 1].Text.Length - lines[i + 1].Text.TrimStart().Length >= content)
                    {
                        item.Add(lines[i] with { Text = string.Empty });
                        i++;
                        continue;
                    }

                    break;
                }

                int lineIndent = text.Length - text.TrimStart().Length;
                if (lineIndent >= content)
                {
                    item.Add(lines[i].Strip(content));
                    i++;
                    continue;
                }

                // A lazy continuation line carries the paragraph on without the indent.
                if (ListMarker(text) is null && !text.TrimStart().StartsWith('>'))
                {
                    item.Add(lines[i] with { Text = text.TrimStart() });
                    i++;
                    continue;
                }

                break;
            }

            AddListItem(item, marker.Value, instance, depth, context, target, quoted, markupDepth);
        }

        return i;
    }

    private static void AddListItem(
        List<Line> item,
        Marker marker,
        int instance,
        int depth,
        ImportContext context,
        IList<Block> target,
        bool quoted,
        int markupDepth)
    {
        var blocks = new List<Block>();
        ParseBlocks(item, 0, item.Count, context, blocks, quoted, depth, markupDepth + 1);

        bool numbered = false;
        foreach (Block block in blocks)
        {
            if (!numbered && block is Paragraph paragraph && paragraph.Format.NumberingId is null)
            {
                paragraph.Format = paragraph.Format with
                {
                    StyleId = context.Document.Styles.GetOrAdd("ListParagraph").Id,
                    NumberingId = instance,
                    NumberingLevel = Math.Min(depth, 8),
                };

                if (marker.Task)
                    paragraph.InsertText(0, marker.TaskDone ? "☒ " : "☐ ");

                numbered = true;
            }
            else if (block is Paragraph follower && follower.Format.NumberingId is null)
            {
                follower.Format = follower.Format with
                {
                    StyleId = context.Document.Styles.GetOrAdd("ListParagraph").Id,
                    IndentLeft = Length.FromTwips(720 * (Math.Min(depth, 8) + 1)),
                };
            }

            target.Add(block);
        }
    }

    private static bool IsTableDelimiter(string line)
    {
        string trimmed = line.Trim();
        if (!trimmed.Contains('-', StringComparison.Ordinal))
            return false;

        foreach (string cell in SplitRow(trimmed))
        {
            string body = cell.Trim();
            if (body.Length == 0)
                return false;

            string bare = body.Trim(':');
            if (bare.Length == 0 || !bare.All(static c => c == '-'))
                return false;
        }

        return true;
    }

    private static int AddTable(List<Line> lines, int at, int to, ImportContext context, IList<Block> target)
    {
        List<string> header = SplitRow(lines[at].Text.Trim());
        List<string> delimiters = SplitRow(lines[at + 1].Text.Trim());

        var alignments = new List<ParagraphAlignment?>();
        foreach (string cell in delimiters)
        {
            string body = cell.Trim();
            bool left = body.StartsWith(':');
            bool right = body.EndsWith(':');
            alignments.Add((left, right) switch
            {
                (true, true) => ParagraphAlignment.Center,
                (false, true) => ParagraphAlignment.Right,
                (true, false) => ParagraphAlignment.Left,
                _ => null,
            });
        }

        var table = new Table();
        table.Format = table.Format with { StyleId = context.Document.Styles.GetOrAdd("TableGrid", StyleKind.Table).Id };

        TableRow head = BuildRow(header, alignments, bold: true, lines[at].Number, context);
        head.Format = head.Format with { IsHeader = true };
        context.Budget.AddMarkupNode();
        table.Rows.Add(head);

        int i = at + 2;
        while (i < to)
        {
            string text = lines[i].Text;
            if (text.Trim().Length == 0 || !text.Contains('|', StringComparison.Ordinal))
                break;

            context.Budget.AddMarkupNode();
            table.Rows.Add(BuildRow(SplitRow(text.Trim()), alignments, bold: false, lines[i].Number, context));
            i++;
        }

        AddBlock(context, target, table);
        return i;
    }

    private static TableRow BuildRow(
        List<string> cells, List<ParagraphAlignment?> alignments, bool bold, int line, ImportContext context)
    {
        var row = new TableRow();
        for (int c = 0; c < Math.Max(cells.Count, alignments.Count); c++)
        {
            var cell = new TableCell();
            var paragraph = new Paragraph();
            if (c < alignments.Count && alignments[c] is { } alignment)
                paragraph.Format = paragraph.Format with { Alignment = alignment };

            string content = c < cells.Count ? cells[c].Trim() : string.Empty;
            context.Parser.Fill(
                paragraph, content, bold ? RunFormat.Default with { Bold = true } : RunFormat.Default, line);
            cell.Blocks.Add(paragraph);
            context.Budget.AddMarkupNode();
            row.Cells.Add(cell);
        }

        return row;
    }

    /// <summary>Splits a table row on the pipes that are neither escaped nor leading nor trailing.</summary>
    private static List<string> SplitRow(string row)
    {
        string body = row;
        if (body.StartsWith('|'))
            body = body[1..];
        if (body.EndsWith('|') && !body.EndsWith("\\|", StringComparison.Ordinal))
            body = body[..^1];

        var cells = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '\\' && i + 1 < body.Length && body[i + 1] == '|')
            {
                current.Append('|');
                i++;
                continue;
            }

            if (c == '|')
            {
                cells.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        cells.Add(current.ToString());
        return cells;
    }

    private static string Snippet(string text) => text.Length <= 40 ? text : text[..40] + "…";

    private static void AddBlock(ImportContext context, IList<Block> target, Block block)
    {
        context.Budget.AddMarkupNode();
        target.Add(block);
    }
}
