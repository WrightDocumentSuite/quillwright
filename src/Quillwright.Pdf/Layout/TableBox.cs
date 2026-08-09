using Inkwright;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>A table after it has been measured: column widths, row heights and the cells between them.</summary>
internal sealed class TableBox : BlockBox
{
    /// <summary>The table this came from.</summary>
    public required Table Source { get; init; }

    /// <summary>The table format after its style chain and direct properties have been resolved.</summary>
    public required TableFormat Format { get; init; }

    /// <summary>The width of each grid column, in points.</summary>
    public required double[] Columns { get; init; }

    /// <summary>The rows, in order.</summary>
    public required List<RowBox> Rows { get; init; }

    /// <summary>How far the table sits from the container's leading edge.</summary>
    public double Offset { get; init; }

    /// <summary>How wide the table is altogether.</summary>
    public double Width => Columns.Sum();

    /// <summary>The rows repeated at the top of every page the table continues onto.</summary>
    public IEnumerable<RowBox> HeaderRows => Rows.TakeWhile(static row => row.IsHeader);

    /// <inheritdoc />
    public override double ContentHeight
    {
        get
        {
            double total = 0;
            foreach (RowBox row in Rows)
                total += row.Height;

            return total;
        }
    }

    /// <summary>The left edge of a grid column, measured from the table's own left edge.</summary>
    /// <param name="column">The grid column index.</param>
    public double LeftOf(int column)
    {
        double x = 0;
        for (int i = 0; i < column && i < Columns.Length; i++)
            x += Columns[i];

        return x;
    }

    /// <summary>The width of a run of grid columns.</summary>
    /// <param name="column">The first grid column.</param>
    /// <param name="span">How many columns are covered.</param>
    public double WidthOf(int column, int span)
    {
        double width = 0;
        for (int i = column; i < column + span && i < Columns.Length; i++)
            width += Columns[i];

        return width;
    }
}

/// <summary>One measured row.</summary>
internal sealed class RowBox
{
    /// <summary>The row this came from.</summary>
    public required TableRow Source { get; init; }

    /// <summary>The row format after table-style and direct properties have been resolved.</summary>
    public required TableRowFormat Format { get; init; }

    /// <summary>The cells anchored in this row, in grid order. A merged continuation is not one.</summary>
    public required List<CellBox> Cells { get; init; }

    /// <summary>How tall the row is.</summary>
    public double Height { get; set; }

    /// <summary>Whether the row repeats at the top of every page the table continues onto.</summary>
    public bool IsHeader { get; init; }

    /// <summary>Whether Word recorded a page boundary immediately before this row.</summary>
    public bool StartsNewPage { get; init; }

    /// <summary>Whether the row may be broken across a page boundary.</summary>
    public bool CanSplit { get; init; } = true;
}

/// <summary>One measured cell, with everything needed to draw it and everything inside it.</summary>
internal sealed class CellBox
{
    /// <summary>The cell this came from.</summary>
    public required TableCell Source { get; init; }

    /// <summary>The cell format after table-style, conditional and direct properties have been resolved.</summary>
    public required TableCellFormat Format { get; init; }

    /// <summary>The grid column the cell starts at.</summary>
    public required int Column { get; init; }

    /// <summary>How many grid columns it covers.</summary>
    public required int Span { get; init; }

    /// <summary>How many rows it covers, counting itself.</summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>The blocks inside it, measured against the width left after the margins.</summary>
    public required List<BlockBox> Content { get; init; }

    /// <summary>Padding inside the cell, in points.</summary>
    public required CellPadding Padding { get; init; }

    /// <summary>Which way the content flows: the ordinary way, or down a turned cell.</summary>
    public TextDirection Direction { get; init; }

    /// <summary>
    /// The height a turned cell asks for, measured off its longest line, or <see langword="null"/>
    /// for the ordinary cells whose height is their stacked content.
    /// </summary>
    public double? NeededOverride { get; set; }

    /// <summary>Where the content sits when it is shorter than the cell.</summary>
    public VerticalCellAlignment VerticalAlignment { get; init; }

    /// <summary>The background of the cell, or <see langword="null"/>.</summary>
    public PdfColor? Fill { get; init; }

    /// <summary>The border on each edge, after the conflicts with neighbours have been settled.</summary>
    public required CellEdges Edges { get; init; }

    /// <summary>How tall the content is, spacing between blocks included.</summary>
    public double ContentHeight
    {
        get
        {
            double total = 0;
            ParagraphBox? previous = null;

            foreach (BlockBox block in Content)
            {
                total += block.ContentHeight + block.SpacingAfter;
                total += previous is null ? block.SpacingBefore : Math.Max(0, block.SpacingBefore - previous.SpacingAfter);
                previous = block as ParagraphBox;
            }

            return total;
        }
    }

    /// <summary>How tall the cell has to be to hold its content.</summary>
    public double NeededHeight => NeededOverride ?? ContentHeight + Padding.Top + Padding.Bottom;
}

/// <summary>The space between a cell's border and its content.</summary>
/// <param name="Left">Space on the leading edge.</param>
/// <param name="Top">Space above.</param>
/// <param name="Right">Space on the trailing edge.</param>
/// <param name="Bottom">Space below.</param>
internal readonly record struct CellPadding(double Left, double Top, double Right, double Bottom)
{
    /// <summary>What Word puts inside a cell when nothing says otherwise.</summary>
    public static CellPadding Default => new(5.4, 0, 5.4, 0);
}

/// <summary>The border to draw on each edge of a cell.</summary>
/// <param name="Left">The leading edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Right">The trailing edge.</param>
/// <param name="Bottom">The bottom edge.</param>
internal readonly record struct CellEdges(BorderLine? Left, BorderLine? Top, BorderLine? Right, BorderLine? Bottom);
