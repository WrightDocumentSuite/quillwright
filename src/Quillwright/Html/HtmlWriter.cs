using System.Globalization;
using System.Text;
using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Rendering;
using Quillwright.Styles;

namespace Quillwright.Html;

/// <summary>
/// Turns the blocks of a document into HTML: headings by outline level, real nested lists
/// from the numbering, tables with their merges as <c>colspan</c> and <c>rowspan</c>, quotes,
/// code, pictures, and the footnotes gathered at the foot of the page.
/// </summary>
internal static class HtmlWriter
{
    public static void WriteDocument(StringBuilder html, HtmlContext context)
    {
        foreach (Section section in context.Document.Sections)
            WriteBlocks(html, section.Blocks, context);

        WriteNotes(html, context);
    }

    public static void WriteBlocks(StringBuilder html, IList<Block> blocks, HtmlContext context)
    {
        int index = 0;
        while (index < blocks.Count)
        {
            switch (blocks[index])
            {
                case Paragraph paragraph when IsListItem(paragraph, context):
                    WriteList(html, blocks, ref index, context);
                    continue;

                case Paragraph paragraph when IsStyled(paragraph, context, QuoteStyles):
                    WriteGroup(html, blocks, ref index, context, QuoteStyles, "<blockquote>\n", "</blockquote>\n", pre: false);
                    continue;

                case Paragraph paragraph when IsStyled(paragraph, context, CodeStyles):
                    WriteGroup(html, blocks, ref index, context, CodeStyles, "<pre><code>", "</code></pre>\n", pre: true);
                    continue;

                case Paragraph paragraph:
                    WriteParagraph(html, paragraph, context);
                    index++;
                    continue;

                case Table table:
                    WriteTable(html, table, context);
                    index++;
                    continue;

                case BlockContentControl control:
                    WriteBlocks(html, control.Blocks, context);
                    index++;
                    continue;

                case AlternateContentBlock alternate:
                    WriteBlocks(html, alternate.Blocks, context);
                    index++;
                    continue;

                default:
                    context.Diagnostics.Add(
                        HtmlExportWarningKind.ContentSkipped,
                        "A preserved block the model does not interpret was skipped.",
                        blocks[index].GetType().Name);
                    index++;
                    continue;
            }
        }
    }

    private static readonly string[] QuoteStyles = ["Quote", "IntenseQuote", "Intense Quote"];
    private static readonly string[] CodeStyles = ["Code", "CodeBlock", "Code Block", "HTMLPreformatted", "HTML Preformatted", "PlainText", "Plain Text"];

    private static void WriteParagraph(StringBuilder html, Paragraph paragraph, HtmlContext context)
    {
        ParagraphFormat format = context.Resolver.ResolveParagraphFormat(paragraph);

        if (paragraph.IsEmpty && format.Borders?.Bottom is { IsEmpty: false })
        {
            html.Append("<hr>\n");
            return;
        }

        int? heading = HeadingLevel(paragraph, format, context);
        string tag = heading is { } level ? "h" + Math.Clamp(level, 1, 6) : "p";
        if (heading is > 6)
        {
            context.Diagnostics.Add(
                HtmlExportWarningKind.StructureApproximated,
                "A heading deeper than six is clamped to h6.",
                "heading-depth");
        }

        html.Append('<').Append(tag);
        AppendParagraphAttributes(html, format);
        html.Append('>');
        HtmlInlineWriter.Render(html, paragraph, context);
        html.Append("</").Append(tag).Append(">\n");
    }

    private static void AppendParagraphAttributes(StringBuilder html, ParagraphFormat format)
    {
        if (format.RightToLeft == true)
            html.Append(" dir=\"rtl\"");

        string? align = format.Alignment switch
        {
            ParagraphAlignment.Center => "center",
            ParagraphAlignment.Right => "right",
            ParagraphAlignment.Justify or ParagraphAlignment.Distribute => "justify",
            _ => null,
        };

        if (align is not null)
            html.Append(" style=\"text-align:").Append(align).Append('"');
    }

    private static int? HeadingLevel(Paragraph paragraph, ParagraphFormat format, HtmlContext context)
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
            return styleLevel;
        }

        string? name = context.Document.Styles.Find(paragraph.Format.StyleId)?.Name;
        if (name is not null && name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.AsSpan("heading ".Length), out int level) && level is >= 1 and <= 9)
        {
            return level;
        }

        return null;
    }

    private static bool IsStyled(Paragraph paragraph, HtmlContext context, string[] styles)
    {
        string? id = paragraph.Format.StyleId;
        if (id is not null && styles.Any(style => style.Equals(id, StringComparison.OrdinalIgnoreCase)))
            return true;

        string? name = context.Document.Styles.Find(id)?.Name;
        return name is not null && styles.Any(style => style.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteGroup(
        StringBuilder html,
        IList<Block> blocks,
        ref int index,
        HtmlContext context,
        string[] styles,
        string open,
        string close,
        bool pre)
    {
        html.Append(open);
        bool first = true;
        while (index < blocks.Count && blocks[index] is Paragraph paragraph && IsStyled(paragraph, context, styles))
        {
            if (pre)
            {
                if (!first)
                    html.Append('\n');
                html.Append(HtmlInlineWriter.Escape(HtmlInlineWriter.Plain(paragraph, context)));
            }
            else
            {
                WriteParagraph(html, paragraph, context);
            }

            first = false;
            index++;
        }

        html.Append(close);
    }

    private static bool IsListItem(Paragraph paragraph, HtmlContext context)
    {
        ParagraphFormat format = context.Resolver.ResolveParagraphFormat(paragraph);
        return format.NumberingId is { } id && id > 0 &&
               context.Document.Numbering.ResolveLevel(id, format.NumberingLevel ?? 0) is { Format: not ListNumberFormat.None };
    }

    /// <summary>
    /// One Word list becomes nested <c>ul</c> and <c>ol</c> elements: consecutive items of one
    /// instance, levels opening and closing as the depth moves, the kind of each level taken
    /// from its own numbering definition, and explicit values where Word's counting departs
    /// from HTML's.
    /// </summary>
    private static void WriteList(StringBuilder html, IList<Block> blocks, ref int index, HtmlContext context)
    {
        var open = new Stack<(string Tag, int Expected)>();
        int firstId = -1;
        int previousLevel = -1;

        while (index < blocks.Count && blocks[index] is Paragraph paragraph)
        {
            ParagraphFormat format = context.Resolver.ResolveParagraphFormat(paragraph);
            if (format.NumberingId is not { } id || id <= 0 ||
                context.Document.Numbering.ResolveLevel(id, format.NumberingLevel ?? 0) is not { } levelDefinition ||
                levelDefinition.Format == ListNumberFormat.None)
            {
                break;
            }

            if (firstId < 0)
                firstId = id;
            else if (id != firstId)
                break;

            int level = Math.Clamp(format.NumberingLevel ?? 0, 0, 8);
            if (previousLevel < 0 && level > 0)
                level = 0;
            else if (previousLevel >= 0 && level > previousLevel + 1)
                level = previousLevel + 1;

            NumberLabel label = context.Lists.Next(format)!.Value;

            while (open.Count - 1 > level)
                CloseList(html, open);

            if (open.Count - 1 < level || open.Count == 0)
                OpenList(html, open, levelDefinition, label.Value);

            (string tag, int expected) = open.Pop();
            html.Append("<li");
            if (tag == "ol" && label.Value != expected)
                html.Append(" value=\"").Append(label.Value.ToString(CultureInfo.InvariantCulture)).Append('"');

            html.Append('>');
            HtmlInlineWriter.Render(html, paragraph, context);
            html.Append("</li>\n");
            open.Push((tag, label.Value + 1));

            previousLevel = level;
            index++;
        }

        while (open.Count > 0)
            CloseList(html, open);
    }

    private static void OpenList(StringBuilder html, Stack<(string Tag, int Expected)> open, NumberingLevel level, int firstValue)
    {
        bool ordered = level.Format is not ListNumberFormat.Bullet;
        string tag = ordered ? "ol" : "ul";
        html.Append('<').Append(tag);
        if (ordered)
        {
            string? type = level.Format switch
            {
                ListNumberFormat.LowerRoman => "i",
                ListNumberFormat.UpperRoman => "I",
                ListNumberFormat.LowerLetter => "a",
                ListNumberFormat.UpperLetter => "A",
                _ => null,
            };

            if (type is not null)
                html.Append(" type=\"").Append(type).Append('"');
            if (firstValue != 1)
                html.Append(" start=\"").Append(firstValue.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        html.Append(">\n");
        open.Push((tag, firstValue));
    }

    private static void CloseList(StringBuilder html, Stack<(string Tag, int Expected)> open) =>
        html.Append("</").Append(open.Pop().Tag).Append(">\n");

    private static void WriteTable(StringBuilder html, Table table, HtmlContext context)
    {
        html.Append("<table>\n");

        List<TableRow> rows = [.. table.Rows.Where(row => MarkdownRevisionView.RowVisible(row, context.RevisionMode))];
        int headerRows = 0;
        while (headerRows < rows.Count && rows[headerRows].Format.IsHeader == true)
            headerRows++;

        if (headerRows > 0)
        {
            html.Append("<thead>\n");
            for (int r = 0; r < headerRows; r++)
                WriteRow(html, rows, r, "th", context);
            html.Append("</thead>\n");
        }

        html.Append("<tbody>\n");
        for (int r = headerRows; r < rows.Count; r++)
            WriteRow(html, rows, r, "td", context);
        html.Append("</tbody>\n</table>\n");
    }

    private static void WriteRow(StringBuilder html, List<TableRow> rows, int rowIndex, string cellTag, HtmlContext context)
    {
        html.Append("<tr>\n");
        int gridColumn = 0;
        foreach (TableCell cell in rows[rowIndex].Cells)
        {
            int span = Math.Max(1, cell.Format.GridSpan ?? 1);
            if (cell.Format.VerticalMerge == VerticalMerge.Continue)
            {
                gridColumn += span;
                continue;
            }

            html.Append('<').Append(cellTag);
            if (span > 1)
                html.Append(" colspan=\"").Append(span.ToString(CultureInfo.InvariantCulture)).Append('"');

            int rowSpan = RowSpan(rows, rowIndex, gridColumn);
            if (rowSpan > 1)
                html.Append(" rowspan=\"").Append(rowSpan.ToString(CultureInfo.InvariantCulture)).Append('"');

            AppendCellStyle(html, cell, context);
            html.Append('>');
            WriteBlocks(html, cell.Blocks, context);
            html.Append("</").Append(cellTag).Append(">\n");
            gridColumn += span;
        }

        html.Append("</tr>\n");
    }

    /// <summary>How many rows a merge starting here covers: this one, plus each continuation below.</summary>
    private static int RowSpan(List<TableRow> rows, int rowIndex, int gridColumn)
    {
        if (rows[rowIndex].Cells.All(static cell => cell.Format.VerticalMerge != VerticalMerge.Restart))
            return 1;

        int span = 1;
        for (int r = rowIndex + 1; r < rows.Count; r++)
        {
            TableCell? below = CellAt(rows[r], gridColumn);
            if (below?.Format.VerticalMerge != VerticalMerge.Continue)
                break;

            span++;
        }

        return span;
    }

    private static TableCell? CellAt(TableRow row, int gridColumn)
    {
        int at = 0;
        foreach (TableCell cell in row.Cells)
        {
            if (at == gridColumn)
                return cell;

            at += Math.Max(1, cell.Format.GridSpan ?? 1);
            if (at > gridColumn)
                return null;
        }

        return null;
    }

    private static void AppendCellStyle(StringBuilder html, TableCell cell, HtmlContext context)
    {
        var css = new StringBuilder();
        if (cell.Format.Shading is { IsEmpty: false } shading && shading.Fill is { IsAuto: false } fill &&
            context.Document.ResolveColor(fill) is { } rgb)
        {
            css.Append("background:#").Append((rgb & 0xFFFFFFu).ToString("x6", CultureInfo.InvariantCulture));
        }

        if (cell.Format.VerticalAlignment is { } vertical && vertical != VerticalCellAlignment.Top)
        {
            if (css.Length > 0)
                css.Append(';');
            css.Append("vertical-align:").Append(vertical == VerticalCellAlignment.Center ? "middle" : "bottom");
        }

        if (css.Length > 0)
            html.Append(" style=\"").Append(css).Append('"');
    }

    private static void WriteNotes(StringBuilder html, HtmlContext context)
    {
        if (context.Notes.Count == 0)
            return;

        html.Append("<hr>\n<section class=\"footnotes\">\n<ol>\n");
        foreach (HtmlNoteEntry note in context.Notes)
        {
            html.Append("<li id=\"").Append(HtmlInlineWriter.Attribute(note.Label)).Append("\">\n");
            WriteBlocks(html, note.Body.Blocks, context);
            html.Append("<a href=\"#").Append(HtmlInlineWriter.Attribute(note.Label)).Append("-ref\">↩</a>\n</li>\n");
        }

        html.Append("</ol>\n</section>\n");
    }
}
