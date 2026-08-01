using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests.Markdown;

public class MarkdownTableTests
{
    [Fact]
    public void SimpleGitHubTable_UsesOneRectangularPipeGrid()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        TableRow header = table.AddRow("Name", "Value");
        header.Format = header.Format with { IsHeader = true };
        ((Paragraph)header.Cells[1].Blocks[0]).Format = ParagraphFormat.Default with
        {
            Alignment = ParagraphAlignment.Right,
        };
        table.AddRow("A|B", "2");
        document.Sections[0].Blocks.Add(table);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal(
            "| Name | Value |\n| --- | ---: |\n| A\\|B | 2 |\n",
            markdown.Text);
        Assert.Empty(markdown.Diagnostics);
    }

    [Fact]
    public void HeaderlessAndRaggedTable_InferHeaderAndPadLogicalCells()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        table.Grid.Add(Length.FromInches(1));
        table.Grid.Add(Length.FromInches(1));
        table.Grid.Add(Length.FromInches(1));
        table.AddRow("A", "B");
        TableRow second = table.AddRow("C", "D");
        second.Format = second.Format with { GridBefore = 1 };
        document.Sections[0].Blocks.Add(table);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal(
            "| A | B |  |\n| --- | --- | --- |\n|  | C | D |\n",
            markdown.Text);
        Assert.Contains(markdown.Diagnostics,
            warning => warning.Subject == "table-header-inference");
    }

    [Fact]
    public void GridSpan_LeavesConsumedPipeColumnsBlankAndDiagnosesApproximation()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        var header = new TableRow { Format = TableRowFormat.Default with { IsHeader = true } };
        header.Cells.Add(Cell("Wide", TableCellFormat.Default with { GridSpan = 2 }));
        header.Cells.Add(Cell("Last"));
        table.Rows.Add(header);
        table.AddRow("A", "B", "C");
        document.Sections[0].Blocks.Add(table);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal(
            "| Wide |  | Last |\n| --- | --- | --- |\n| A | B | C |\n",
            markdown.Text);
        Assert.Contains(markdown.Diagnostics,
            warning => warning.Subject == "table-colspan");
    }

    [Fact]
    public void VerticalMerge_UsesEncodedHtmlWithRowspanAndColspan()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        var first = new TableRow { Format = TableRowFormat.Default with { IsHeader = true } };
        first.Cells.Add(Cell("<unsafe>", TableCellFormat.Default with
        {
            GridSpan = 2,
            VerticalMerge = VerticalMerge.Restart,
        }));
        first.Cells.Add(Cell("Tail"));
        table.Rows.Add(first);
        var second = new TableRow();
        second.Cells.Add(Cell("ignored", TableCellFormat.Default with
        {
            GridSpan = 2,
            VerticalMerge = VerticalMerge.Continue,
        }));
        second.Cells.Add(Cell("Body"));
        table.Rows.Add(second);
        document.Sections[0].Blocks.Add(table);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Contains("<table>\n<thead>", markdown.Text, StringComparison.Ordinal);
        Assert.Contains("<th colspan=\"2\" rowspan=\"2\"><p>&lt;unsafe&gt;</p></th>",
            markdown.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored", markdown.Text, StringComparison.Ordinal);
        Assert.Contains(markdown.Diagnostics,
            warning => warning.Kind == MarkdownExportWarningKind.HtmlFallbackUsed);
    }

    [Fact]
    public void MultipleCellParagraphs_ForceHtmlAndPreserveEveryBlock()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        var row = new TableRow();
        TableCell cell = Cell("first");
        cell.AddParagraph("second");
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Sections[0].Blocks.Add(table);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Contains("<td><p>first</p>\n<p>second</p></td>", markdown.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("| --- |", markdown.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CommonMarkFlavor_AlwaysUsesHtmlForTables()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        table.AddRow("A", "B");
        document.Sections[0].Blocks.Add(table);

        MarkdownDocument markdown = document.ToMarkdown(new MarkdownExportOptions
        {
            Flavor = MarkdownFlavor.CommonMark,
        });

        Assert.StartsWith("<table>\n<tbody>", markdown.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("| --- |", markdown.Text, StringComparison.Ordinal);
        Assert.Contains(markdown.Diagnostics,
            warning => warning.Subject == "commonmark-table");
    }

    [Fact]
    public void HiddenAndTrackedRows_FollowTheSelectedRevisionView()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        table.AddRow("Header");
        TableRow hidden = table.AddRow("Hidden");
        hidden.Format = hidden.Format with { Hidden = true };
        TableRow inserted = table.AddRow("Inserted");
        inserted.Format = inserted.Format with { InsertedXml = "<w:ins w:id=\"1\"/>" };
        TableRow deleted = table.AddRow("Deleted");
        deleted.Format = deleted.Format with { DeletedXml = "<w:del w:id=\"2\"/>" };
        document.Sections[0].Blocks.Add(table);

        MarkdownDocument accepted = document.ToMarkdown();
        MarkdownDocument original = document.ToMarkdown(new MarkdownExportOptions
        {
            RevisionMode = MarkdownRevisionMode.Original,
        });

        Assert.Contains("Inserted", accepted.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Deleted", accepted.Text, StringComparison.Ordinal);
        Assert.Contains("Deleted", original.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Inserted", original.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", accepted.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", original.Text, StringComparison.Ordinal);
    }

    private static TableCell Cell(string text, TableCellFormat? format = null)
    {
        var cell = new TableCell();
        if (format is { } value)
            cell.Format = value;
        cell.AddParagraph(text);
        return cell;
    }
}
