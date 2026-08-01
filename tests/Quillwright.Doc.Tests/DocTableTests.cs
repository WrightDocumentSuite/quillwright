using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Tables are the part of the format with no element of their own: they are a run of
/// paragraphs whose marks happen to say where the cells and rows end. Everything here checks
/// that the structure survives being flattened and rebuilt.
/// </summary>
public class DocTableTests
{
    [Fact]
    public void CellText_LandsInTheRightCell()
    {
        Table table = RoundTrip(NewTable(["a", "b", "c"], ["d", "e", "f"]));

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["a", "b", "c"], table.Rows[0].Cells.Select(static c => c.GetText().Trim()));
        Assert.Equal(["d", "e", "f"], table.Rows[1].Cells.Select(static c => c.GetText().Trim()));
    }

    [Fact]
    public void ACellWithSeveralParagraphs_KeepsThemTogether()
    {
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Blocks.Add(new Paragraph("first"));
        cell.Blocks.Add(new Paragraph("second"));
        row.Cells.Add(cell);
        var other = new TableCell();
        other.Blocks.Add(new Paragraph("alone"));
        row.Cells.Add(other);
        table.Rows.Add(row);

        Table reopened = RoundTrip(table);

        Assert.Equal(2, reopened.Rows[0].Cells.Count);
        Assert.Equal(2, reopened.Rows[0].Cells[0].Blocks.OfType<Paragraph>().Count());
        Assert.Equal("alone", reopened.Rows[0].Cells[1].GetText().Trim());
    }

    [Fact]
    public void AHorizontalSpan_SurvivesTheRoundTrip()
    {
        var table = new Table();
        table.Grid.Add(Length.FromTwips(2000));
        table.Grid.Add(Length.FromTwips(2000));
        table.Grid.Add(Length.FromTwips(2000));

        var row = new TableRow();
        var wide = new TableCell { Format = TableCellFormat.Default with { GridSpan = 2 } };
        wide.Blocks.Add(new Paragraph("wide"));
        row.Cells.Add(wide);
        var narrow = new TableCell();
        narrow.Blocks.Add(new Paragraph("narrow"));
        row.Cells.Add(narrow);
        table.Rows.Add(row);

        Table reopened = RoundTrip(table);

        Assert.Equal(2, reopened.Rows[0].Cells.Count);
        Assert.Equal(2, reopened.Rows[0].Cells[0].Format.GridSpan);
        Assert.Null(reopened.Rows[0].Cells[1].Format.GridSpan);
    }

    [Fact]
    public void AVerticalMerge_SurvivesTheRoundTrip()
    {
        var table = new Table();
        table.Rows.Add(NewRow(("top", VerticalMerge.Restart), ("other", null)));
        table.Rows.Add(NewRow(("", VerticalMerge.Continue), ("more", null)));

        Table reopened = RoundTrip(table);

        Assert.Equal(VerticalMerge.Restart, reopened.Rows[0].Cells[0].Format.VerticalMerge);
        Assert.Equal(VerticalMerge.Continue, reopened.Rows[1].Cells[0].Format.VerticalMerge);
    }

    [Fact]
    public void ColumnWidths_SurviveTheRoundTrip()
    {
        var table = new Table();
        table.Grid.Add(Length.FromTwips(1440));
        table.Grid.Add(Length.FromTwips(2880));
        table.Rows.Add(NewRow(("narrow", null), ("wide", null)));

        Table reopened = RoundTrip(table);

        Assert.Equal(Length.FromTwips(1440), reopened.Rows[0].Cells[0].Format.Width!.Value.Length);
        Assert.Equal(Length.FromTwips(2880), reopened.Rows[0].Cells[1].Format.Width!.Value.Length);
    }

    [Fact]
    public void AHeaderRow_SurvivesTheRoundTrip()
    {
        var table = new Table();
        TableRow header = NewRow(("head", null));
        header.Format = TableRowFormat.Default with { IsHeader = true, CannotSplit = true };
        table.Rows.Add(header);
        table.Rows.Add(NewRow(("body", null)));

        Table reopened = RoundTrip(table);

        Assert.True(reopened.Rows[0].Format.IsHeader);
        Assert.True(reopened.Rows[0].Format.CannotSplit);
        Assert.NotEqual(true, reopened.Rows[1].Format.IsHeader);
    }

    [Fact]
    public void ATableInsideACell_SurvivesTheRoundTrip()
    {
        var inner = new Table();
        inner.Rows.Add(NewRow(("deep", null)));

        var outer = new Table();
        var row = new TableRow();
        var host = new TableCell();
        host.Blocks.Add(new Paragraph("before"));
        host.Blocks.Add(inner);
        host.Blocks.Add(new Paragraph("after"));
        row.Cells.Add(host);
        outer.Rows.Add(row);

        Table reopened = RoundTrip(outer);
        Table nested = reopened.Rows[0].Cells[0].Blocks.OfType<Table>().Single();

        Assert.Equal("deep", nested.Rows[0].Cells[0].GetText().Trim());
        Assert.Contains(reopened.Rows[0].Cells[0].Blocks.OfType<Paragraph>(), p => p.Text.Contains("before", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoTablesSeparatedByAParagraph_StayTwoTables()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(NewTable(["one"]));
        document.Sections[0].Blocks.Add(new Paragraph("between"));
        document.Sections[0].Blocks.Add(NewTable(["two"]));
        document.Sections[0].Blocks.Add(new Paragraph("after"));

        List<Block> blocks = [.. DocReader.Load(DocWriter.Save(document)).Sections[0].Blocks];

        Assert.Equal(2, blocks.OfType<Table>().Count());
        Assert.Contains(blocks.OfType<Paragraph>(), p => p.Text == "between");
    }

    [Fact]
    public void CellShading_SurvivesTheRoundTrip()
    {
        var table = new Table();
        var row = new TableRow();
        var shaded = new TableCell
        {
            Format = TableCellFormat.Default with { Shading = Shading.Solid(WordColor.FromRgb(0xFFFF00)) },
        };

        shaded.Blocks.Add(new Paragraph("yellow"));
        row.Cells.Add(shaded);
        var plain = new TableCell();
        plain.Blocks.Add(new Paragraph("plain"));
        row.Cells.Add(plain);
        table.Rows.Add(row);

        Table reopened = RoundTrip(table);

        Assert.Equal(WordColor.FromRgb(0xFFFF00), reopened.Rows[0].Cells[0].Format.Shading!.Fill);
        Assert.Null(reopened.Rows[0].Cells[1].Format.Shading);
    }

    [Fact]
    public void EachRowKeepsItsOwnCellShading()
    {
        var table = new Table();
        table.Rows.Add(ShadedRow(WordColor.FromRgb(0xFF0000)));
        table.Rows.Add(ShadedRow(WordColor.FromRgb(0x0000FF)));

        Table reopened = RoundTrip(table);

        Assert.Equal(WordColor.FromRgb(0xFF0000), reopened.Rows[0].Cells[0].Format.Shading!.Fill);
        Assert.Equal(WordColor.FromRgb(0x0000FF), reopened.Rows[1].Cells[0].Format.Shading!.Fill);
    }

    [Fact]
    public void AWideTable_StillFitsBecauseItsPropertiesMoveToTheDataStream()
    {
        // Sixty-three columns make the row definition well over a kilobyte, which cannot sit
        // in a formatting page and has to be stored indirectly instead.
        var table = new Table();
        var row = new TableRow();
        for (int i = 0; i < 63; i++)
        {
            var cell = new TableCell();
            cell.Blocks.Add(new Paragraph($"c{i}"));
            row.Cells.Add(cell);
        }

        table.Rows.Add(row);

        Table reopened = RoundTrip(table);

        Assert.Equal(63, reopened.Rows[0].Cells.Count);
        Assert.Equal("c62", reopened.Rows[0].Cells[62].GetText().Trim());
    }

    private static Table RoundTrip(Table table)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(table);
        document.Sections[0].Blocks.Add(new Paragraph("after the table"));

        return DocReader.Load(DocWriter.Save(document))
            .Sections.SelectMany(static s => s.Blocks)
            .OfType<Table>()
            .Single();
    }

    private static TableRow ShadedRow(WordColor fill)
    {
        var row = new TableRow();
        var cell = new TableCell { Format = TableCellFormat.Default with { Shading = Shading.Solid(fill) } };
        cell.Blocks.Add(new Paragraph("shaded"));
        row.Cells.Add(cell);
        return row;
    }

    private static TableRow NewRow(params (string Text, VerticalMerge? Merge)[] cells)
    {
        var row = new TableRow();
        foreach ((string text, VerticalMerge? merge) in cells)
        {
            var cell = new TableCell();
            if (merge is { } value)
                cell.Format = cell.Format with { VerticalMerge = value };
            cell.Blocks.Add(new Paragraph(text));
            row.Cells.Add(cell);
        }

        return row;
    }

    private static Table NewTable(params string[][] rows)
    {
        var table = new Table();
        foreach (string[] cells in rows)
            table.Rows.Add(NewRow([.. cells.Select(static c => (c, (VerticalMerge?)null))]));
        return table;
    }
}
