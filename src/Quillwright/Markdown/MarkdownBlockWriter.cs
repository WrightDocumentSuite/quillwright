using System.Text;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Markdown;

/// <summary>Writes block containers in their model order.</summary>
internal static class MarkdownBlockWriter
{
    public static void WriteDocument(StringBuilder builder, MarkdownContext context)
    {
        foreach (Section section in context.Document.Sections)
        {
            WriteBlocks(builder, section.Blocks, context);
            if (section.Headers.Defined.Any(static item => item.Content.Blocks.Count > 0) ||
                section.Footers.Defined.Any(static item => item.Content.Blocks.Count > 0))
            {
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.ContentSkipped,
                    "Headers and footers are page-dependent stories and are not included in Markdown body order.",
                    "headers-and-footers");
            }
        }

        if (context.Document.Comments.Any(static comment => comment.Blocks.Count > 0))
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.ContentSkipped,
                "Comments and replies are not included in the Markdown body.",
                "comments");
        }

        if (context.Document.Charts.Count > 0 || context.Document.EmbeddedObjects.Count > 0)
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.ContentSkipped,
                "Charts and embedded objects are not exported to Markdown.",
                "embedded-content");
        }
    }

    public static string RenderBlocks(IEnumerable<Block> blocks, MarkdownContext context)
    {
        var builder = new StringBuilder();
        WriteBlocks(builder, blocks is IList<Block> list ? list : blocks.ToArray(), context);
        return TrimBlock(builder.ToString());
    }

    public static void WriteBlocks(
        StringBuilder builder,
        IList<Block> blocks,
        MarkdownContext context)
    {
        for (int index = 0; index < blocks.Count;)
        {
            Block block = blocks[index];
            if (block is Paragraph listParagraph && IsList(listParagraph, context))
            {
                string list = MarkdownListWriter.Render(blocks, ref index, context);
                AppendBlock(builder, list);
                continue;
            }

            if (block is Paragraph code && IsCode(code, context))
            {
                string fenced = RenderCodeGroup(blocks, ref index, context);
                AppendBlock(builder, fenced);
                continue;
            }

            if (block is Paragraph quote && IsQuote(quote, context))
            {
                string quoted = RenderQuoteGroup(blocks, ref index, context);
                AppendBlock(builder, quoted);
                continue;
            }

            index++;
            switch (block)
            {
                case Paragraph paragraph:
                    AppendBlock(builder, RenderParagraphWithJoins(paragraph, blocks, ref index, context));
                    break;
                case Table table:
                    AppendBlock(builder, MarkdownTableWriter.Render(table, context));
                    break;
                case BlockContentControl control:
                    context.Diagnostics.Add(
                        MarkdownExportWarningKind.StructureApproximated,
                        "A block content-control wrapper is omitted while its content is preserved.",
                        "block-content-control");
                    WriteBlocks(builder, control.Blocks, context);
                    break;
                case AlternateContentBlock alternate:
                    context.Diagnostics.Add(
                        MarkdownExportWarningKind.StructureApproximated,
                        "A compatibility block is written as the branch a reader of this vocabulary shows.",
                        "alternate-content");
                    WriteBlocks(builder, alternate.Blocks, context);
                    break;
                case RawBlock:
                    context.Diagnostics.Add(
                        MarkdownExportWarningKind.ContentSkipped,
                        "Raw block-level OOXML cannot be copied into Markdown safely and was skipped.",
                        "raw-block");
                    break;
                default:
                    context.Diagnostics.Add(
                        MarkdownExportWarningKind.ContentSkipped,
                        "An unsupported block was skipped.",
                        block.GetType().Name);
                    break;
            }
        }
    }

    public static string RenderParagraph(Paragraph paragraph, MarkdownContext context, bool insideList = false)
    {
        ParagraphFormat format = context.Resolver.ResolveParagraphFormat(paragraph);
        if (paragraph.Format.ChangeXml is not null)
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.FormattingDropped,
                "Historical paragraph-format revisions cannot be reconstructed from preserved raw XML.",
                "format-revision");
        }

        string content = MarkdownInlineWriter.Render(paragraph, context);
        if (content.Length == 0)
            return string.Empty;

        if (HeadingLevel(paragraph, format, context) is { } heading)
        {
            int level = Math.Clamp(heading, 1, 6);
            if (heading > 6)
            {
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.StructureApproximated,
                    "Heading levels deeper than six are clamped to H6.",
                    "heading-depth");
            }

            string oneLine = content.Replace("  \n", " ", StringComparison.Ordinal)
                .Replace("\n\n", " ", StringComparison.Ordinal)
                .Replace('\n', ' ');
            return new string('#', level) + " " + oneLine;
        }

        return content;
    }

    public static void AppendBlock(StringBuilder builder, string block)
    {
        block = TrimBlock(block);
        if (block.Length == 0)
            return;

        while (builder.Length > 0 && builder[^1] == '\n')
            builder.Length--;
        if (builder.Length > 0)
            builder.Append("\n\n");
        builder.Append(block);
    }

    private static string RenderParagraphWithJoins(
        Paragraph first,
        IList<Block> blocks,
        ref int index,
        MarkdownContext context)
    {
        var result = new StringBuilder(RenderParagraph(first, context));
        Paragraph current = first;
        while (MarkdownRevisionView.JoinsNext(current, context.Options.RevisionMode) &&
               index < blocks.Count && blocks[index] is Paragraph next)
        {
            result.Append(MarkdownInlineWriter.Render(next, context));
            current = next;
            index++;
        }

        return result.ToString();
    }

    private static string RenderQuoteGroup(
        IList<Block> blocks,
        ref int index,
        MarkdownContext context)
    {
        var body = new StringBuilder();
        while (index < blocks.Count && blocks[index] is Paragraph paragraph && IsQuote(paragraph, context))
        {
            if (body.Length > 0)
                body.Append("\n\n");
            body.Append(RenderParagraph(paragraph, context));
            index++;
        }

        return PrefixLines(body.ToString(), "> ", emptyPrefix: ">");
    }

    private static string RenderCodeGroup(
        IList<Block> blocks,
        ref int index,
        MarkdownContext context)
    {
        var lines = new List<string>();
        while (index < blocks.Count && blocks[index] is Paragraph paragraph && IsCode(paragraph, context))
        {
            lines.AddRange(MarkdownInlineWriter.RenderPlain(paragraph, context).Split('\n'));
            index++;
        }

        (char fenceCharacter, int fenceLength) = MarkdownText.Fence(lines);
        string fence = new(fenceCharacter, fenceLength);
        return fence + "\n" + string.Join('\n', lines) + "\n" + fence;
    }

    private static bool IsList(Paragraph paragraph, MarkdownContext context)
    {
        ParagraphFormat format = context.Resolver.ResolveParagraphFormat(paragraph);
        return format.NumberingId is > 0 &&
               context.Document.Numbering.ResolveLevel(format.NumberingId.Value, format.NumberingLevel ?? 0) is not null;
    }

    private static bool IsQuote(Paragraph paragraph, MarkdownContext context)
    {
        string? id = paragraph.Format.StyleId;
        if (id is not null && (id.Equals("Quote", StringComparison.OrdinalIgnoreCase) ||
                               id.Equals("IntenseQuote", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string? name = context.Document.Styles.Find(id)?.Name;
        return name is not null && (name.Equals("Quote", StringComparison.OrdinalIgnoreCase) ||
                                    name.Equals("Intense Quote", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCode(Paragraph paragraph, MarkdownContext context)
    {
        string? id = paragraph.Format.StyleId;
        if (id is not null && id.EqualsAny("Code", "CodeBlock", "HTMLPreformatted", "PlainText"))
            return true;

        string? name = context.Document.Styles.Find(id)?.Name;
        if (name is not null && name.EqualsAny("Code", "Code Block", "HTML Preformatted", "Plain Text"))
            return true;

        bool sawText = false;
        foreach (Run run in paragraph.Runs)
        {
            if (run.Kind is not RunKind.Text || run.Span.IsWhiteSpace())
                continue;
            sawText = true;
            RunFormat format = context.Resolver.ResolveRunFormat(run);
            string family = string.Join(' ', format.FontAscii, format.FontHighAnsi).ToLowerInvariant();
            if (!family.Contains("courier", StringComparison.Ordinal) &&
                !family.Contains("mono", StringComparison.Ordinal) &&
                !family.Contains("consol", StringComparison.Ordinal) &&
                !family.Contains("menlo", StringComparison.Ordinal) &&
                !family.Contains("monaco", StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (sawText)
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.StructureApproximated,
                "A paragraph whose visible runs are all monospaced is treated as a code block.",
                "monospace-code-heuristic");
        }

        return sawText;
    }

    private static int? HeadingLevel(Paragraph paragraph, ParagraphFormat format, MarkdownContext context)
    {
        if (format.OutlineLevel is { } outline and >= 0)
            return outline + 1;
        if (paragraph.Format.StyleId?.Equals("Title", StringComparison.OrdinalIgnoreCase) == true)
            return 1;

        if (paragraph.Format.StyleId is { } styleId &&
            styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(styleId.AsSpan("Heading".Length), out int styleLevel) &&
            styleLevel is >= 1 and <= 9)
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.StructureApproximated,
                "A heading was inferred from its built-in style id because the style definition is absent.",
                "heading-style-id");
            return styleLevel;
        }

        string? name = context.Document.Styles.Find(paragraph.Format.StyleId)?.Name;
        if (name is not null && name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.AsSpan("heading ".Length), out int level) && level is >= 1 and <= 9)
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.StructureApproximated,
                "A heading was inferred from its style name because no outline level was resolved.",
                "heading-style-name");
            return level;
        }

        return null;
    }

    private static string PrefixLines(string text, string prefix, string emptyPrefix)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        return string.Join('\n', lines.Select(line => line.Length == 0 ? emptyPrefix : prefix + line));
    }

    private static string TrimBlock(string block) => block.Trim('\r', '\n');

    private static bool EqualsAny(this string value, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (value.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
