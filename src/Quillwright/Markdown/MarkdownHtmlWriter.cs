using System.Globalization;
using System.Text;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Markdown;

/// <summary>Generated, encoded HTML for constructs the selected Markdown dialect cannot express.</summary>
internal static class MarkdownHtmlWriter
{
    public static string RenderInline(Paragraph paragraph, MarkdownContext context)
    {
        var builder = new StringBuilder();
        Hyperlink? openLink = null;
        bool linkOpened = false;

        foreach (MarkdownInlineToken token in MarkdownInlineWalker.Walk(paragraph, context))
        {
            if (!ReferenceEquals(openLink, token.Link))
            {
                if (linkOpened)
                    builder.Append("</a>");
                openLink = token.Link;
                linkOpened = false;
                if (openLink is not null && MarkdownInlineWriter.Destination(openLink, context) is { } destination)
                {
                    builder.Append("<a href=\"").Append(MarkdownText.HtmlAttribute(destination)).Append("\">");
                    linkOpened = true;
                }
            }

            WriteToken(builder, token, context);
        }

        if (linkOpened)
            builder.Append("</a>");
        return builder.ToString();
    }

    public static string RenderBlocks(IEnumerable<Block> blocks, MarkdownContext context)
    {
        var builder = new StringBuilder();
        Block[] items = [.. blocks];
        for (int i = 0; i < items.Length;)
        {
            if (items[i] is Paragraph paragraph && IsList(paragraph, context))
            {
                WriteList(builder, items, ref i, context);
                continue;
            }

            Block block = items[i++];
            switch (block)
            {
                case Paragraph text:
                    builder.Append("<p>").Append(RenderInline(text, context)).Append("</p>\n");
                    break;
                case Table table:
                    builder.Append(MarkdownTableWriter.RenderHtml(table, context)).Append('\n');
                    break;
                case BlockContentControl control:
                    context.Diagnostics.Add(
                        MarkdownExportWarningKind.StructureApproximated,
                        "A block content-control wrapper is omitted while its content is preserved.",
                        "block-content-control");
                    builder.Append(RenderBlocks(control.Blocks, context));
                    break;
                case AlternateContentBlock alternate:
                    builder.Append(RenderBlocks(alternate.Blocks, context));
                    break;
                case RawBlock:
                    context.Diagnostics.Add(
                        MarkdownExportWarningKind.ContentSkipped,
                        "Raw block-level OOXML cannot be copied into generated HTML safely and was skipped.",
                        "raw-block");
                    break;
            }
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static void WriteToken(StringBuilder builder, MarkdownInlineToken token, MarkdownContext context)
    {
        switch (token.Kind)
        {
            case MarkdownInlineKind.Text:
                builder.Append(Styled(MarkdownText.HtmlText(token.Text), token.Style));
                break;
            case MarkdownInlineKind.LineBreak:
                builder.Append("<br>");
                break;
            case MarkdownInlineKind.BlockBreak:
                builder.Append("<br><br>");
                break;
            case MarkdownInlineKind.Tab:
                builder.Append(' ');
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.StructureApproximated,
                    "A tab outside a code block is represented by one space.",
                    "tab");
                break;
            case MarkdownInlineKind.Anchor:
                builder.Append("<a id=\"").Append(MarkdownText.HtmlAttribute(token.Text)).Append("\"></a>");
                break;
            case MarkdownInlineKind.Picture when token.Picture is { } picture:
                WritePicture(builder, picture, context);
                break;
            case MarkdownInlineKind.NoteReference when token.NoteReference is { } reference:
                if (context.Notes.Add(reference) is { } note)
                {
                    builder.Append("<sup><a href=\"#").Append(MarkdownText.HtmlAttribute(note.Label)).Append("\">")
                        .Append(note.Number.ToString(CultureInfo.InvariantCulture)).Append("</a></sup>");
                }
                else
                {
                    builder.Append("<sup>?</sup>");
                }

                break;
        }
    }

    private static string Styled(string text, MarkdownInlineStyle style)
    {
        string result = text;
        if (style.Code)
            return "<code>" + result + "</code>";
        if (style.Bold)
            result = "<strong>" + result + "</strong>";
        if (style.Italic)
            result = "<em>" + result + "</em>";
        if (style.Strike)
            result = "<del>" + result + "</del>";
        if (style.Underline)
            result = "<ins>" + result + "</ins>";
        return style.VerticalAlignment switch
        {
            VerticalTextAlignment.Superscript => "<sup>" + result + "</sup>",
            VerticalTextAlignment.Subscript => "<sub>" + result + "</sub>",
            _ => result,
        };
    }

    private static void WritePicture(StringBuilder builder, Picture picture, MarkdownContext context)
    {
        string file = context.Media.Add(picture.Image);
        string reference = context.Media.Reference(file);
        string alt = !string.IsNullOrWhiteSpace(picture.Description)
            ? picture.Description
            : !string.IsNullOrWhiteSpace(picture.Name) ? picture.Name : file;

        builder.Append("<img src=\"").Append(MarkdownText.HtmlAttribute(reference))
            .Append("\" alt=\"").Append(MarkdownText.HtmlAttribute(alt)).Append('"');
        if (context.Options.PreserveImageDimensions && picture.Width.Twips > 0 && picture.Height.Twips > 0)
        {
            int width = Math.Max(1, (int)Math.Round(picture.Width.ToPixels(), MidpointRounding.AwayFromZero));
            int height = Math.Max(1, (int)Math.Round(picture.Height.ToPixels(), MidpointRounding.AwayFromZero));
            builder.Append(" width=\"").Append(width.ToString(CultureInfo.InvariantCulture))
                .Append("\" height=\"").Append(height.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        builder.Append('>');
    }

    private static bool IsList(Paragraph paragraph, MarkdownContext context)
    {
        ParagraphFormat format = context.Resolver.ResolveParagraphFormat(paragraph);
        return format.NumberingId is > 0 &&
               context.Document.Numbering.ResolveLevel(format.NumberingId.Value, format.NumberingLevel ?? 0) is not null;
    }

    private static void WriteList(StringBuilder builder, Block[] blocks, ref int index, MarkdownContext context)
    {
        Paragraph first = (Paragraph)blocks[index];
        ParagraphFormat firstFormat = context.Resolver.ResolveParagraphFormat(first);
        NumberingLevel firstLevel = context.Document.Numbering.ResolveLevel(
            firstFormat.NumberingId!.Value, firstFormat.NumberingLevel ?? 0)!;
        bool ordered = firstLevel.Format != ListNumberFormat.Bullet;
        string tag = ordered ? "ol" : "ul";
        builder.Append('<').Append(tag).Append(">\n");

        int listId = firstFormat.NumberingId.Value;
        while (index < blocks.Length && blocks[index] is Paragraph paragraph)
        {
            ParagraphFormat format = context.Resolver.ResolveParagraphFormat(paragraph);
            if (format.NumberingId != listId ||
                context.Document.Numbering.ResolveLevel(listId, format.NumberingLevel ?? 0) is not { } level ||
                (level.Format != ListNumberFormat.Bullet) != ordered)
            {
                break;
            }

            if ((format.NumberingLevel ?? 0) > 0)
            {
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.StructureApproximated,
                    "Nested list depth inside an HTML fallback container is flattened.",
                    "html-list-depth");
            }

            builder.Append("<li>").Append(RenderInline(paragraph, context)).Append("</li>\n");
            index++;
        }

        builder.Append("</").Append(tag).Append(">\n");
    }
}
