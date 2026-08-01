using Quillwright.Editing;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Samples;

/// <summary>Builds a document from nothing: headings, styled text, a table, a header and a footer.</summary>
internal static class BuildFromScratch
{
    public static async Task RunAsync(string directory)
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Title = "Supply agreement";
        document.Properties.Creator = "Quillwright";

        var editor = new DocumentEditor(document);
        editor.WriteHeading("Supply agreement", 1)
            .WriteLine("This agreement is made between the parties named below.")
            .WriteHeading("Parties", 2);

        int list = document.Numbering.AddBulletList();
        foreach (string party in (string[])["Supplier: Quillwright Works Ltd", "Customer: Example GmbH"])
        {
            Paragraph paragraph = document.Sections[0].AddParagraph(party, "ListParagraph");
            paragraph.Format = paragraph.Format with { NumberingId = list, NumberingLevel = 0 };
        }

        editor.MoveTo(document.Sections[0]).WriteHeading("Schedule of goods", 2);

        Table table = document.Sections[0].AddTable(1, 3);
        table.Format = table.Format with { StyleId = "TableGrid" };
        document.Styles.GetOrAdd("TableGrid", StyleKind.Table);
        table.Rows[0].Format = table.Rows[0].Format with { IsHeader = true };
        FillRow(table.Rows[0], RunFormat.Default with { Bold = true }, "Item", "Quantity", "Price");
        FillRow(table.AddRow(), RunFormat.Default, "Widget", "12", "19.50");
        FillRow(table.AddRow(), RunFormat.Default, "Gadget", "4", "7.25");

        Paragraph closing = document.Sections[0].AddParagraph();
        closing.AppendText("Signed on ");
        closing.AppendDate();
        closing.AppendText(".");

        document.Sections[0].Headers.GetOrCreate().AddParagraph("Supply agreement", "Header");
        Paragraph footer = document.Sections[0].Footers.GetOrCreate().AddParagraph(string.Empty, "Footer");
        footer.AppendText("Page ");
        footer.AppendPageNumber();
        footer.AppendText(" of ");
        footer.AppendPageCount();

        string path = Path.Combine(directory, "01-from-scratch.docx");
        await document.SaveAsync(path);
        Console.WriteLine($"  built {Path.GetFileName(path)}");
    }

    private static void FillRow(TableRow row, RunFormat format, params string[] values)
    {
        for (int i = 0; i < values.Length && i < row.Cells.Count; i++)
        {
            row.Cells[i].SetText(values[i], format);
            row.Cells[i].Format = row.Cells[i].Format with { Width = TableWidth.FromLength(Length.FromCentimeters(5)) };
        }
    }
}
