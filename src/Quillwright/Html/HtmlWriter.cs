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
    private static readonly string[] ListContinuationStyles = ["ListParagraph", "List Paragraph"];

    private static void WriteParagraph(StringBuilder html, Paragraph paragraph, HtmlContext context)
    {
        ParagraphFormat format = context.ResolveParagraphFormat(paragraph);

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
        ParagraphFormat format = context.ResolveParagraphFormat(paragraph);
        return format.NumberingId is { } id && id > 0 &&
               context.Numbering.ResolveLevel(id, format.NumberingLevel ?? 0) is not null;
    }

    /// <summary>
    /// One logical Word list becomes nested <c>ul</c> and <c>ol</c> elements: levels open and
    /// close as the depth moves, nested lists may use their own numbering instances, and
    /// restart instances stay in the same HTML list through explicit item values.
    /// </summary>
    private static void WriteList(StringBuilder html, IList<Block> blocks, ref int index, HtmlContext context)
    {
        var open = new List<OpenListState>();
        int previousLevel = -1;

        while (index < blocks.Count && blocks[index] is Paragraph paragraph)
        {
            int continuationLevel = ListContinuationLevel(paragraph, open, context);
            if (continuationLevel >= 0)
            {
                while (open.Count - 1 > continuationLevel)
                    CloseList(html, open);

                WriteParagraph(html, paragraph, context);
                previousLevel = continuationLevel;
                index++;
                continue;
            }

            ParagraphFormat format = context.ResolveParagraphFormat(paragraph);
            if (format.NumberingId is not { } id || id <= 0 ||
                context.Numbering.ResolveLevel(id, format.NumberingLevel ?? 0) is not { } levelDefinition)
            {
                break;
            }

            int level = Math.Clamp(format.NumberingLevel ?? 0, 0, 8);
            if (previousLevel < 0 && level > 0)
                level = 0;
            else if (previousLevel >= 0 && level > previousLevel + 1)
                level = previousLevel + 1;

            if (open.Count > 0 && !BelongsToOpenList(open, id, level, previousLevel, context))
            {
                // Two different lists at the same nested depth are siblings owned by the
                // still-open parent li. Close only the old nested list, not its owner.
                if (level == 0 || level >= open.Count || !open[level - 1].ItemOpen)
                    break;
                while (open.Count > level)
                    CloseList(html, open);
                previousLevel = level - 1;
            }

            NumberLabel label = context.Lists.Next(format)!.Value;

            while (open.Count - 1 > level)
                CloseList(html, open);

            if (open.Count == 0 || open.Count - 1 < level)
            {
                if (open.Count > 0)
                    html.Append('\n');
                OpenList(html, open, levelDefinition, label.Value, id, context);
            }
            else
            {
                CloseItem(html, open[level]);
            }

            OpenListState current = open[level];
            current.NumberingIds.Add(id);
            string itemMarker = HtmlListStyle.FromLevel(levelDefinition);
            html.Append("<li");
            if (current.Tag == "ol" && label.Value != current.Expected)
                html.Append(" value=\"").Append(label.Value.ToString(CultureInfo.InvariantCulture)).Append('"');
            if (itemMarker != current.Marker)
                html.Append(" style=\"list-style-type:").Append(itemMarker).Append('"');

            html.Append('>');
            HtmlInlineWriter.Render(html, paragraph, context);
            current.ItemOpen = true;
            current.Expected = label.Value + 1;

            previousLevel = level;
            index++;
        }

        while (open.Count > 0)
            CloseList(html, open);
    }

    private static int ListContinuationLevel(
        Paragraph paragraph,
        List<OpenListState> open,
        HtmlContext context)
    {
        if (open.Count == 0 || !IsStyled(paragraph, context, ListContinuationStyles))
            return -1;

        ParagraphFormat format = context.ResolveParagraphFormat(paragraph);
        if (format.NumberingId is not null || format.IndentLeft is not { } indent)
            return -1;

        for (int level = open.Count - 1; level >= 0; level--)
        {
            if (open[level].ItemOpen && open[level].ContinuationIndentTwips == indent.Twips)
                return level;
        }

        return -1;
    }

    private static bool BelongsToOpenList(
        List<OpenListState> open,
        int numberingId,
        int level,
        int previousLevel,
        HtmlContext context)
    {
        if (level < open.Count && open[level].NumberingIds.Contains(numberingId))
            return true;

        // A deeper paragraph starts a nested list. Its numbering instance can differ from
        // its parent's: HTML gives each nested ul/ol its own start and marker kind.
        if (level > previousLevel)
            return true;

        if (level >= open.Count)
            return false;

        OpenListState current = open[level];
        NumberingInstance? instance = context.Numbering.FindInstance(numberingId);
        bool restart = instance?.AbstractId == current.AbstractId &&
                       instance.Overrides.Any(candidate =>
                           candidate.Level == level && candidate.StartOverride is not null);
        if (restart)
            current.NumberingIds.Add(numberingId);

        return restart;
    }

    private static void OpenList(
        StringBuilder html,
        List<OpenListState> open,
        NumberingLevel level,
        int firstValue,
        int numberingId,
        HtmlContext context)
    {
        NumberingLevel containerLevel = context.Numbering.ResolveDefinition(numberingId)?
            .Levels.FirstOrDefault(candidate => candidate.Level == level.Level) ?? level;
        string marker = HtmlListStyle.FromLevel(containerLevel);
        bool ordered = containerLevel.Format is not (ListNumberFormat.Bullet or ListNumberFormat.None);
        string tag = ordered ? "ol" : "ul";
        html.Append('<').Append(tag);
        if (ordered)
        {
            string? type = marker switch
            {
                "lower-roman" => "i",
                "upper-roman" => "I",
                "lower-latin" => "a",
                "upper-latin" => "A",
                _ => null,
            };

            if (type is not null)
                html.Append(" type=\"").Append(type).Append('"');
            if (firstValue != 1)
                html.Append(" start=\"").Append(firstValue.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        if (marker is "decimal-leading-zero" or "circle" or "square" or "none")
            html.Append(" style=\"list-style-type:").Append(marker).Append('"');

        html.Append(">\n");
        int abstractId = context.Numbering.FindInstance(numberingId)?.AbstractId ?? -1;
        open.Add(new OpenListState(
            tag,
            marker,
            firstValue,
            numberingId,
            abstractId,
            containerLevel.ParagraphFormat.IndentLeft?.Twips));
    }

    private static void CloseItem(StringBuilder html, OpenListState open)
    {
        if (!open.ItemOpen)
            return;

        html.Append("</li>\n");
        open.ItemOpen = false;
    }

    private static void CloseList(StringBuilder html, List<OpenListState> open)
    {
        OpenListState current = open[^1];
        CloseItem(html, current);
        html.Append("</").Append(current.Tag).Append(">\n");
        open.RemoveAt(open.Count - 1);
    }

    private sealed class OpenListState(
        string tag,
        string marker,
        int expected,
        int numberingId,
        int abstractId,
        int? continuationIndentTwips)
    {
        public string Tag { get; } = tag;

        public string Marker { get; } = marker;

        public int Expected { get; set; } = expected;

        public int AbstractId { get; } = abstractId;

        public bool ItemOpen { get; set; }

        public int? ContinuationIndentTwips { get; } = continuationIndentTwips;

        public HashSet<int> NumberingIds { get; } = [numberingId];
    }

    private static void WriteTable(StringBuilder html, Table table, HtmlContext context)
    {
        html.Append("<table>\n");
        if (!string.IsNullOrWhiteSpace(table.Format.Caption))
        {
            html.Append("<caption>").Append(HtmlInlineWriter.Escape(table.Format.Caption))
                .Append("</caption>\n");
        }

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

        // A note body may itself reference a later note. Render every body first, allowing
        // the queue to grow, so the final emission sees all notes and every reciprocal link.
        // Delaying the <li> output also lets a later body add a backlink to an earlier note.
        var bodies = new List<string>();
        for (int index = 0; index < context.Notes.Count; index++)
        {
            var body = new StringBuilder();
            WriteBlocks(body, context.Notes[index].Body.Blocks, context);
            bodies.Add(body.ToString());
        }

        html.Append("<hr>\n<section class=\"footnotes\">\n<ol>\n");
        for (int noteIndex = 0; noteIndex < context.Notes.Count; noteIndex++)
        {
            HtmlNoteEntry note = context.Notes[noteIndex];
            html.Append("<li id=\"").Append(HtmlInlineWriter.Attribute(note.Label)).Append("\">\n");
            html.Append(bodies[noteIndex]);
            for (int index = 0; index < note.ReferenceLabels.Count; index++)
            {
                if (index > 0)
                    html.Append(' ');
                html.Append("<a href=\"#").Append(HtmlInlineWriter.Attribute(note.ReferenceLabels[index]))
                    .Append("\">↩</a>");
            }
            html.Append("\n</li>\n");
        }

        html.Append("</ol>\n</section>\n");
    }
}
