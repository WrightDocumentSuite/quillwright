using Quillwright.Model;
using Quillwright.Styles;
using Quillwright.Templates;

namespace Quillwright.Samples;

/// <summary>A typed template model. The source generator writes its binder.</summary>
[WordTemplate]
public partial record InvoiceLine(
    [property: TemplateField("Description")] string Text,
    int Quantity,
    [property: TemplateField(Format = "N2")] decimal Amount);

/// <summary>The model a template is filled from.</summary>
[WordTemplate]
public partial record Invoice
{
    public required string Customer { get; init; }

    [TemplateField("Number")]
    public required string InvoiceNumber { get; init; }

    [TemplateField(Format = "dd.MM.yyyy")]
    public DateTime Issued { get; init; }

    [TemplateRows("Lines")]
    public IReadOnlyList<InvoiceLine> Lines { get; init; } = [];

    [TemplateCondition("ShowTerms")]
    public bool HasTerms { get; init; }
}

/// <summary>Builds a template document and then fills it from a model.</summary>
internal static class FillTemplate
{
    public static async Task RunAsync(string directory)
    {
        string templatePath = Path.Combine(directory, "03-template.docx");
        await BuildTemplateAsync(templatePath);

        var invoice = new Invoice
        {
            Customer = "Beispiel AG",
            InvoiceNumber = "INV-2026-0042",
            Issued = new DateTime(2026, 3, 14),
            HasTerms = true,
            Lines =
            [
                new InvoiceLine("Widget", 12, 19.5m),
                new InvoiceLine("Gadget", 4, 7.25m),
                new InvoiceLine("Doohickey", 1, 120m),
            ],
        };

        string path = Path.Combine(directory, "04-invoice.docx");
        TemplateResult result = await WordTemplateEngine.RenderAsync(templatePath, invoice, path);
        Console.WriteLine($"  filled {Path.GetFileName(path)} ({result.ValuesFilled} value(s), {result.RegionsRepeated} row(s))");
    }

    private static async Task BuildTemplateAsync(string path)
    {
        WordDocument template = WordDocument.Create();
        template.Sections[0].AddParagraph("Invoice {{Number}}", "Heading1");
        template.Sections[0].AddParagraph("Billed to {{Customer}} on {{Issued}}.");

        Table table = template.Sections[0].AddTable(2, 3);
        table.Format = table.Format with { StyleId = "TableGrid" };
        template.Styles.GetOrAdd("TableGrid", StyleKind.Table);
        table[0, 0].SetText("Description", RunFormat.Default with { Bold = true });
        table[0, 1].SetText("Qty", RunFormat.Default with { Bold = true });
        table[0, 2].SetText("Amount", RunFormat.Default with { Bold = true });
        table[1, 0].SetText("{{Lines.Description}}");
        table[1, 1].SetText("{{Lines.Quantity}}");
        table[1, 2].SetText("{{Lines.Amount}}");

        var terms = new BlockContentControl { Tag = "if:ShowTerms", Alias = "Payment terms" };
        terms.Blocks.Add(new Paragraph("Payment is due within 30 days."));
        template.Sections[0].Blocks.Add(terms);

        await template.SaveAsync(path);
    }
}
