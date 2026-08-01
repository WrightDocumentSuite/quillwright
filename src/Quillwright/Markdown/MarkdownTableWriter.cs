using System.Text;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Markdown;

/// <summary>Writes simple tables as GFM pipes and complex/CommonMark tables as generated HTML.</summary>
internal static class MarkdownTableWriter
{
    public static string Render(Table table, MarkdownContext context)
    {
        List<LogicalRow> rows = Grid(table, context, out int columns);
        if (rows.Count == 0 || columns == 0)
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.ContentSkipped,
                "An empty table was skipped.",
                "empty-table");
            return string.Empty;
        }

        if (table.Format.ChangeXml is not null)
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.FormattingDropped,
                "Historical table-format revisions cannot be reconstructed from preserved raw XML.",
                "format-revision");
        }

        if (CanUsePipes(rows, context))
            return RenderPipes(table, rows, columns, context);

        context.Diagnostics.Add(
            MarkdownExportWarningKind.HtmlFallbackUsed,
            context.Options.Flavor == MarkdownFlavor.CommonMark
                ? "CommonMark has no table syntax, so the table uses generated HTML."
                : "A table with merges or block content uses generated HTML.",
            context.Options.Flavor == MarkdownFlavor.CommonMark ? "commonmark-table" : "complex-table");
        return RenderHtml(table, context, rows, columns);
    }

    internal static string RenderHtml(Table table, MarkdownContext context)
    {
        List<LogicalRow> rows = Grid(table, context, out int columns);
        return rows.Count == 0 || columns == 0 ? string.Empty : RenderHtml(table, context, rows, columns);
    }

    private static string RenderPipes(
        Table table,
        List<LogicalRow> rows,
        int columns,
        MarkdownContext context)
    {
        if (rows[0].Source.Format.IsHeader != true)
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.StructureApproximated,
                "The first table row is inferred as the mandatory GFM header.",
                "table-header-inference");
        }

        var builder = new StringBuilder();
        string? caption = Caption(table);
        if (caption is not null)
            builder.Append('*').Append(MarkdownText.Escape(caption)).Append("*\n\n");

        WritePipeRow(builder, rows[0], columns, context);
        builder.Append('|');
        for (int column = 0; column < columns; column++)
            builder.Append(' ').Append(Delimiter(Alignment(rows[0].Slots[column], context))).Append(" |");
        builder.Append('\n');

        for (int row = 1; row < rows.Count; row++)
            WritePipeRow(builder, rows[row], columns, context);

        if (rows.Any(static row => row.Slots.Any(static slot => slot.IsSpanContinuation)))
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.StructureApproximated,
                "Horizontal cell spans are represented by empty GFM cells.",
                "table-colspan");
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static void WritePipeRow(
        StringBuilder builder,
        LogicalRow row,
        int columns,
        MarkdownContext context)
    {
        builder.Append('|');
        for (int column = 0; column < columns; column++)
        {
            LogicalSlot slot = row.Slots[column];
            string content = slot.Cell is { } cell && !slot.IsSpanContinuation
                ? MarkdownInlineWriter.Render((Paragraph)cell.Blocks[0], context, tableCell: true)
                : string.Empty;
            builder.Append(' ').Append(content).Append(" |");
        }

        builder.Append('\n');
    }

    private static string RenderHtml(
        Table table,
        MarkdownContext context,
        List<LogicalRow> rows,
        int columns)
    {
        var builder = new StringBuilder();
        builder.Append("<table>\n");
        if (Caption(table) is { } caption)
            builder.Append("<caption>").Append(MarkdownText.HtmlText(caption)).Append("</caption>\n");

        int headerRows = 0;
        while (headerRows < rows.Count && rows[headerRows].Source.Format.IsHeader == true)
            headerRows++;

        if (headerRows > 0)
        {
            builder.Append("<thead>\n");
            for (int row = 0; row < headerRows; row++)
                WriteHtmlRow(builder, rows, row, columns, header: true, context);
            builder.Append("</thead>\n");
        }

        if (headerRows < rows.Count)
        {
            builder.Append("<tbody>\n");
            for (int row = headerRows; row < rows.Count; row++)
                WriteHtmlRow(builder, rows, row, columns, header: false, context);
            builder.Append("</tbody>\n");
        }

        builder.Append("</table>");
        return builder.ToString();
    }

    private static void WriteHtmlRow(
        StringBuilder builder,
        List<LogicalRow> rows,
        int rowIndex,
        int columns,
        bool header,
        MarkdownContext context)
    {
        LogicalRow row = rows[rowIndex];
        builder.Append("<tr>\n");
        for (int column = 0; column < columns; column++)
        {
            LogicalSlot slot = row.Slots[column];
            if (slot.IsSpanContinuation || slot.Cell?.Format.VerticalMerge == VerticalMerge.Continue)
                continue;

            string tag = header ? "th" : "td";
            builder.Append('<').Append(tag);
            if (slot.Span > 1)
                builder.Append(" colspan=\"").Append(slot.Span).Append('"');
            int rowspan = RowSpan(rows, rowIndex, column);
            if (rowspan > 1)
                builder.Append(" rowspan=\"").Append(rowspan).Append('"');
            builder.Append('>');

            if (slot.Cell is { } cell)
            {
                if (cell.Format.RevisionXml is not null)
                {
                    context.Diagnostics.Add(
                        MarkdownExportWarningKind.StructureApproximated,
                        "Cell revision markup cannot reconstruct the historical table grid; the current grid is used.",
                        "table-cell-revision");
                }

                builder.Append(MarkdownHtmlWriter.RenderBlocks(cell.Blocks, context));
            }

            builder.Append("</").Append(tag).Append(">\n");
            column += Math.Max(1, slot.Span) - 1;
        }

        builder.Append("</tr>\n");
    }

    private static int RowSpan(List<LogicalRow> rows, int rowIndex, int column)
    {
        LogicalSlot slot = rows[rowIndex].Slots[column];
        if (slot.Cell?.Format.VerticalMerge != VerticalMerge.Restart)
            return 1;

        int span = 1;
        for (int row = rowIndex + 1; row < rows.Count; row++)
        {
            LogicalSlot below = rows[row].Slots[column];
            if (below.Cell?.Format.VerticalMerge != VerticalMerge.Continue)
                break;
            span++;
        }

        return span;
    }

    private static bool CanUsePipes(List<LogicalRow> rows, MarkdownContext context)
    {
        if (context.Options.Flavor != MarkdownFlavor.GitHub)
            return false;

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            LogicalRow row = rows[rowIndex];
            if (rowIndex > 0 && row.Source.Format.IsHeader == true)
                return false;

            foreach (LogicalSlot slot in row.Slots)
            {
                if (slot.Cell is not { } cell)
                    continue;
                if ((cell.Format.VerticalMerge is { } merge && merge != VerticalMerge.None) ||
                    cell.Format.HorizontalMergeXml is not null ||
                    cell.Blocks.Count != 1 || cell.Blocks[0] is not Paragraph)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static List<LogicalRow> Grid(Table table, MarkdownContext context, out int columns)
    {
        List<TableRow> visible =
        [
            .. table.Rows.Where(row => row.Format.Hidden != true &&
                MarkdownRevisionView.RowVisible(row, context.Options.RevisionMode)),
        ];
        columns = table.ColumnCount;
        foreach (TableRow row in visible)
        {
            int width = row.Format.GridBefore ?? 0;
            width += row.Cells.Sum(static cell => Math.Max(1, cell.Format.GridSpan ?? 1));
            width += row.Format.GridAfter ?? 0;
            if (width > columns)
            {
                columns = width;
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.StructureApproximated,
                    "A table row extends beyond the declared grid; the logical grid is widened.",
                    "table-grid-width");
            }
        }

        var result = new List<LogicalRow>(visible.Count);
        foreach (TableRow row in visible)
        {
            var slots = Enumerable.Range(0, columns).Select(static _ => new LogicalSlot()).ToArray();
            int column = Math.Clamp(row.Format.GridBefore ?? 0, 0, columns);
            foreach (TableCell cell in row.Cells)
            {
                if (column >= columns)
                    break;
                int span = Math.Clamp(cell.Format.GridSpan ?? 1, 1, columns - column);
                slots[column] = new LogicalSlot { Cell = cell, Span = span };
                for (int swallowed = 1; swallowed < span; swallowed++)
                {
                    slots[column + swallowed] = new LogicalSlot
                    {
                        Cell = cell,
                        IsSpanContinuation = true,
                    };
                }

                column += span;
            }

            result.Add(new LogicalRow(row, slots));
        }

        return result;
    }

    private static ParagraphAlignment? Alignment(LogicalSlot slot, MarkdownContext context)
    {
        if (slot.Cell?.Blocks.FirstOrDefault() is not Paragraph paragraph)
            return null;
        return context.Resolver.ResolveParagraphFormat(paragraph).Alignment;
    }

    private static string Delimiter(ParagraphAlignment? alignment) => alignment switch
    {
        ParagraphAlignment.Left => ":---",
        ParagraphAlignment.Center => ":---:",
        ParagraphAlignment.Right => "---:",
        _ => "---",
    };

    private static string? Caption(Table table) => !string.IsNullOrWhiteSpace(table.Format.Caption)
        ? table.Format.Caption
        : !string.IsNullOrWhiteSpace(table.Format.Description) ? table.Format.Description : null;

    private sealed record LogicalRow(TableRow Source, LogicalSlot[] Slots);

    private sealed class LogicalSlot
    {
        public TableCell? Cell { get; init; }

        public int Span { get; init; } = 1;

        public bool IsSpanContinuation { get; init; }
    }
}
