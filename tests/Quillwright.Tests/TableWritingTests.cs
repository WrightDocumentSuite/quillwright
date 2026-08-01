using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// Tables that were built through the API rather than read from a file, which is where the
/// gaps are: a loaded table brings its own grid and its own properties, while a constructed
/// one leaves the writer to work out what Word needs.
/// </summary>
public class TableWritingTests
{
    [Fact]
    public async Task ATableWithNoGrid_StillGetsColumnsWithAWidth()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(Cell("a"));
        row.Cells.Add(Cell("b"));
        table.Rows.Add(row);
        document.Sections[0].Blocks.Add(table);

        string xml = await MarkupAsync(document);

        // An unsized grid is legal but useless: Word lays out from the grid, so the table
        // would be drawn with no width at all.
        Assert.DoesNotContain("<w:gridCol/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:gridCol w:w=\"4680\"/><w:gridCol w:w=\"4680\"/>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ColumnWidthsDeclaredOnCells_BecomeTheGrid()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(Cell("narrow", TableCellFormat.Default with { Width = TableWidth.FromLength(Length.FromTwips(1440)) }));
        row.Cells.Add(Cell("wide", TableCellFormat.Default with { Width = TableWidth.FromLength(Length.FromTwips(2880)) }));
        table.Rows.Add(row);
        document.Sections[0].Blocks.Add(table);

        string xml = await MarkupAsync(document);

        Assert.Contains("<w:gridCol w:w=\"1440\"/><w:gridCol w:w=\"2880\"/>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASpanningCellsWidth_IsSplitAcrossTheColumnsItCovers()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(Cell("spans", TableCellFormat.Default with
        {
            GridSpan = 2,
            Width = TableWidth.FromLength(Length.FromTwips(4000)),
        }));
        row.Cells.Add(Cell("single", TableCellFormat.Default with { Width = TableWidth.FromLength(Length.FromTwips(1000)) }));
        table.Rows.Add(row);
        document.Sections[0].Blocks.Add(table);

        string xml = await MarkupAsync(document);

        Assert.Contains("<w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"1000\"/>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATableInsideACell_SurvivesWithItsStructure()
    {
        WordDocument document = WordDocument.Create();
        var outer = new Table();
        var outerRow = new TableRow();
        var host = new TableCell();
        var inner = new Table();
        var innerRow = new TableRow();
        innerRow.Cells.Add(Cell("nested"));
        inner.Rows.Add(innerRow);
        host.Blocks.Add(new Paragraph("before"));
        host.Blocks.Add(inner);
        host.Blocks.Add(new Paragraph("after"));
        outerRow.Cells.Add(host);
        outer.Rows.Add(outerRow);
        document.Sections[0].Blocks.Add(outer);

        WordDocument reopened = await RoundTripAsync(document);
        Table result = reopened.Sections[0].Blocks.OfType<Table>().Single();
        Table nested = result.Rows[0].Cells[0].Blocks.OfType<Table>().Single();

        Assert.Equal("nested", nested.Rows[0].Cells[0].GetText());
        Assert.Equal(2, result.Rows[0].Cells[0].Blocks.OfType<Paragraph>().Count());
    }

    [Fact]
    public async Task SpansMergesAndHeaderRows_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        table.Grid.Add(Length.FromTwips(2000));
        table.Grid.Add(Length.FromTwips(2000));
        table.Grid.Add(Length.FromTwips(2000));

        var first = new TableRow { Format = TableRowFormat.Default with { IsHeader = true, CannotSplit = true } };
        first.Cells.Add(Cell("spans two", TableCellFormat.Default with { GridSpan = 2, VerticalMerge = VerticalMerge.Restart }));
        first.Cells.Add(Cell("plain"));
        table.Rows.Add(first);

        var second = new TableRow();
        second.Cells.Add(Cell("merged", TableCellFormat.Default with { VerticalMerge = VerticalMerge.Continue }));
        second.Cells.Add(Cell("b"));
        second.Cells.Add(Cell("c"));
        table.Rows.Add(second);

        document.Sections[0].Blocks.Add(table);

        Table result = (await RoundTripAsync(document)).Sections[0].Blocks.OfType<Table>().Single();

        Assert.True(result.Rows[0].Format.IsHeader);
        Assert.True(result.Rows[0].Format.CannotSplit);
        Assert.Equal(2, result.Rows[0].Cells[0].Format.GridSpan);
        Assert.Equal(VerticalMerge.Restart, result.Rows[0].Cells[0].Format.VerticalMerge);
        Assert.Equal(VerticalMerge.Continue, result.Rows[1].Cells[0].Format.VerticalMerge);
        Assert.Equal([2000, 2000, 2000], result.Grid.Select(static width => width.Twips));
    }

    [Fact]
    public async Task ATableInAHeader_IsWrittenIntoTheHeaderPart()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));
        HeaderFooter header = document.Sections[0].Headers.GetOrCreate();
        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(Cell("in the header"));
        table.Rows.Add(row);
        header.Blocks.Add(table);

        WordDocument reopened = await RoundTripAsync(document);
        Table result = reopened.Sections[0].Headers.Default!.Blocks.OfType<Table>().Single();

        Assert.Equal("in the header", result.Rows[0].Cells[0].GetText());
    }

    private static TableCell Cell(string text, TableCellFormat? format = null)
    {
        var cell = new TableCell();
        if (format is { } value)
            cell.Format = value;
        cell.Blocks.Add(new Paragraph(text));
        return cell;
    }

    private static async Task<string> MarkupAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "table markup");
        return OpenXmlAssert.ReadPart(buffer, "document.xml");
    }

    private static async Task<WordDocument> RoundTripAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "table round trip");
        buffer.Position = 0;
        return await WordDocument.LoadAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
    }
}
