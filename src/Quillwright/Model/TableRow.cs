using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>One row of a table.</summary>
public sealed class TableRow
{
    /// <summary>Creates an empty row.</summary>
    public TableRow() => Cells = new OwnedList<TableCell>(cell => cell.Row = this, cell => cell.Row = null);

    /// <summary>The table this row belongs to, or <see langword="null"/> when detached.</summary>
    public Table? Table { get; internal set; }

    /// <summary>Row-level formatting (<c>w:trPr</c>).</summary>
    public TableRowFormat Format { get; set; } = TableRowFormat.Default;

    /// <summary>The cells of the row, in order.</summary>
    public OwnedList<TableCell> Cells { get; }

    /// <summary>Attributes of <c>w:tr</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>
    /// The row's overrides of the table's own formatting (<c>w:tblPrEx</c>), kept verbatim.
    /// </summary>
    /// <remarks>
    /// A row carries these when it was pasted from a table with different borders or margins,
    /// and the schema requires them before everything else in the row.
    /// </remarks>
    public string? PropertyExceptionsXml { get; set; }

    /// <summary>
    /// Children of <c>w:tr</c> this version does not model — a bookmark or a permission mark
    /// between two cells — kept verbatim and written after the last cell.
    /// </summary>
    public string? PreservedXml { get; set; }

    /// <summary>Appends a cell holding one paragraph with the given text.</summary>
    /// <param name="text">Text of the cell, or <see langword="null"/> for an empty paragraph.</param>
    public TableCell AddCell(string? text = null)
    {
        var cell = new TableCell();
        cell.AddParagraph(text);
        Cells.Add(cell);
        return cell;
    }

    /// <summary>Returns an independent copy of the row, not attached to any table.</summary>
    public TableRow Clone()
    {
        var clone = new TableRow
        {
            Format = Format,
            Attributes = Attributes,
            PropertyExceptionsXml = PropertyExceptionsXml,
            PreservedXml = PreservedXml,
        };

        foreach (TableCell cell in Cells)
            clone.Cells.Add(cell.Clone());
        return clone;
    }
}

/// <summary>One cell of a table row. A cell holds blocks, so it can contain nested tables.</summary>
public sealed class TableCell : BlockContainer
{
    /// <summary>The row this cell belongs to, or <see langword="null"/> when detached.</summary>
    public TableRow? Row { get; internal set; }

    /// <summary>Cell-level formatting (<c>w:tcPr</c>).</summary>
    public TableCellFormat Format { get; set; } = TableCellFormat.Default;

    /// <summary>Attributes of <c>w:tc</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <inheritdoc />
    public override WordDocument? Document => Row?.Table?.Document;

    /// <summary>Replaces the content of the cell with a single paragraph of text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="format">Character formatting of the run.</param>
    public TableCell SetText(string text, RunFormat? format = null)
    {
        Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.AppendText(text, format);
        Blocks.Add(paragraph);
        return this;
    }

    /// <summary>Returns an independent copy of the cell, not attached to any row.</summary>
    public TableCell Clone()
    {
        var clone = new TableCell { Format = Format, Attributes = Attributes };
        foreach (Block block in Blocks)
            clone.Blocks.Add(block.Clone());
        return clone;
    }
}
