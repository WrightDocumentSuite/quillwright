using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>Rebuilds tables from the flat run of paragraphs the format stores them as.</summary>
internal static partial class DocConverter
{
    /// <summary>
    /// Rebuilds tables from the flat run of paragraphs the format stores them as.
    /// </summary>
    /// <remarks>
    /// Nesting is recovered from the depth each paragraph declares rather than from any
    /// containment in the file: when the depth rises a table is opened, when it falls the
    /// innermost table is closed and dropped into the cell that was collecting content.
    /// </remarks>
    private static void BuildBlocks(Section section, List<DocParagraph> paragraphs)
    {
        var open = new Stack<TableLevel>();
        var blocks = new List<Block>();

        foreach (DocParagraph entry in paragraphs)
        {
            int depth = Depth(entry);
            while (open.Count > depth)
                Close(open, blocks);
            while (open.Count < depth)
                open.Push(new TableLevel());

            if (depth == 0)
            {
                blocks.Add(entry.Paragraph);
                continue;
            }

            TableLevel level = open.Peek();
            if (EndsRow(entry, depth))
            {
                level.CloseRow(entry);
                continue;
            }

            level.Cell.Blocks.Add(entry.Paragraph);
            if (EndsCell(entry, depth))
                level.CloseCell();
        }

        while (open.Count > 0)
            Close(open, blocks);

        foreach (Block block in blocks)
            section.Blocks.Add(block);
    }

    private static int Depth(DocParagraph entry) =>
        entry.Flags is { InTable: false, IsRowEnd: false } ? 0 : Math.Max(1, entry.Flags.TableDepth);

    private static bool EndsRow(DocParagraph entry, int depth) =>
        depth == 1 ? entry.Flags.IsRowEnd : entry.Flags.EndsInnerRow;

    private static bool EndsCell(DocParagraph entry, int depth) =>
        depth == 1 ? entry.EndsCell : entry.Flags.EndsInnerCell;

    /// <summary>Closes the innermost open table into whatever was collecting content around it.</summary>
    private static void Close(Stack<TableLevel> open, List<Block> blocks)
    {
        Table? table = open.Pop().Build();
        if (table is null)
            return;

        if (open.Count > 0)
            open.Peek().Cell.Blocks.Add(table);
        else
            blocks.Add(table);
    }

    /// <summary>One table being reassembled, at one nesting depth.</summary>
    private sealed class TableLevel
    {
        private readonly List<TableCell> _cells = [];
        private readonly List<TableRow> _rows = [];
        private readonly List<List<DocTableCell>> _definitions = [];

        public TableCell Cell { get; private set; } = new();

        public void CloseCell()
        {
            _cells.Add(Cell);
            Cell = new TableCell();
        }

        public void CloseRow(DocParagraph mark)
        {
            if (Cell.Blocks.Count > 0)
                CloseCell();
            if (_cells.Count == 0)
                return;

            var row = new TableRow { Format = RowFormat(mark) };
            foreach (TableCell cell in _cells)
                row.Cells.Add(cell);

            _rows.Add(row);
            _definitions.Add(DocTableDefinition.Read(mark.Properties));
            _cells.Clear();
        }

        public Table? Build()
        {
            if (_rows.Count == 0)
                return null;

            var table = new Table
            {
                Format = TableFormat.Default with
                {
                    Borders = BorderSet.AllWithInside(BorderLine.Single(Primitives.Length.FromEighthPoints(4), Primitives.WordColor.Auto)),
                    StyleOptions = TableStyleOptions.Default,
                },
            };

            for (int i = 0; i < _rows.Count; i++)
            {
                ApplyDefinition(_rows[i], _definitions[i]);
                foreach (TableCell cell in _rows[i].Cells)
                {
                    if (cell.Blocks.Count == 0)
                        cell.AddParagraph();
                }

                table.Rows.Add(_rows[i]);
            }

            foreach (Primitives.Length width in Grid(_definitions))
                table.Grid.Add(width);
            return table;
        }

        private static TableRowFormat RowFormat(DocParagraph mark)
        {
            var format = TableRowFormat.Default;
            var reader = new SprmReader(mark.Properties);
            while (reader.TryRead(out Sprm sprm))
            {
                format = sprm.Opcode switch
                {
                    SprmCode.TableHeaderRow => format with { IsHeader = sprm.Byte != 0 },
                    SprmCode.TableCannotSplit => format with { CannotSplit = sprm.Byte != 0 },
                    _ => format,
                };
            }

            return format;
        }

        /// <summary>
        /// Merges the row definition back into the cells. The definition has one entry per
        /// grid column, so a span of columns collapses into the cell that started it.
        /// </summary>
        private static void ApplyDefinition(TableRow row, List<DocTableCell> definition)
        {
            if (definition.Count == 0)
                return;

            int cellIndex = 0;
            for (int column = 0; column < definition.Count && cellIndex < row.Cells.Count; cellIndex++)
            {
                int span = 1;
                Primitives.Length width = definition[column].Width;
                while (column + span < definition.Count && definition[column + span].ContinuesHorizontalSpan)
                {
                    width += definition[column + span].Width;
                    span++;
                }

                row.Cells[cellIndex].Format = row.Cells[cellIndex].Format with
                {
                    Width = TableWidth.FromLength(width),
                    GridSpan = span > 1 ? span : null,
                    VerticalMerge = definition[column].VerticalMerge,
                    VerticalAlignment = definition[column].VerticalAlignment,
                    Shading = definition[column].Shading,
                };

                column += span;
            }
        }

        private static List<Primitives.Length> Grid(List<List<DocTableCell>> definitions)
        {
            List<DocTableCell> widest = definitions.Count == 0
                ? []
                : definitions.MaxBy(static d => d.Count) ?? [];
            return [.. widest.Select(static cell => cell.Width)];
        }
    }

}
