using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Templates.Tests;

public class TemplateEngineTests
{
    [Fact]
    public void Generator_ProducesABinderThatReadsEveryRole()
    {
        ITemplateBinder binder = Invoice.TemplateBinder;
        var invoice = new Invoice
        {
            Customer = "ООО «Ромашка»",
            InvoiceNumber = "INV-7",
            Issued = new DateTime(2026, 3, 14),
            HasNotes = true,
            Lines = [new InvoiceLine("Widget", 2, 19.5m)],
        };

        Assert.True(binder.TryGetText(invoice, "Customer", out string? customer));
        Assert.Equal("ООО «Ромашка»", customer);

        Assert.True(binder.TryGetText(invoice, "Number", out string? number));
        Assert.Equal("INV-7", number);

        Assert.True(binder.TryGetText(invoice, "Issued", out string? issued));
        Assert.Equal("14.03.2026", issued);

        Assert.True(binder.TryGetCondition(invoice, "ShowNotes", out bool notes));
        Assert.True(notes);

        Assert.True(binder.TryGetRows(invoice, "Lines", out TemplateRows rows));
        Assert.Single(rows.Items);
        Assert.True(rows.Binder.TryGetText(rows.Items[0], "Description", out string? description));
        Assert.Equal("Widget", description);

        Assert.False(binder.TryGetText(invoice, "Missing", out _));
    }

    [Fact]
    public void Render_FillsPlaceholdersSplitAcrossRuns()
    {
        WordDocument template = WordDocument.Create();
        Paragraph paragraph = template.Sections[0].AddParagraph();
        paragraph.AppendText("Invoice ");
        paragraph.AppendText("{{Num", RunFormat.Default with { Bold = true });
        paragraph.AppendText("ber}} for ", RunFormat.Default);
        paragraph.AppendText("{{Customer}}");

        TemplateResult result = WordTemplateEngine.Render(template, Sample());

        Assert.Equal("Invoice INV-7 for ООО «Ромашка»", paragraph.Text);
        Assert.Equal(2, result.ValuesFilled);
        Assert.Empty(result.UnresolvedNames);
    }

    [Fact]
    public void Render_RepeatsATableRowPerItem()
    {
        WordDocument template = WordDocument.Create();
        Table table = template.Sections[0].AddTable(2, 3);
        table[0, 0].SetText("Description");
        table[0, 1].SetText("Qty");
        table[0, 2].SetText("Amount");
        table[1, 0].SetText("{{Lines.Description}}");
        table[1, 1].SetText("{{Lines.Quantity}}");
        table[1, 2].SetText("{{Lines.Amount}}");

        TemplateResult result = WordTemplateEngine.Render(template, Sample());

        Assert.Equal(3, result.RegionsRepeated);
        Assert.Equal(4, table.Rows.Count);
        Assert.Equal("Widget", table[1, 0].GetText());
        Assert.Equal("Gadget", table[2, 0].GetText());
        Assert.Equal("3", table[3, 1].GetText());
        Assert.Equal("7.25", table[3, 2].GetText());
    }

    [Fact]
    public void Render_KeepsOrDropsAConditionalRegion()
    {
        Assert.Equal(2, RenderConditional(hasNotes: true).Sections[0].Blocks.Count);
        Assert.Single(RenderConditional(hasNotes: false).Sections[0].Blocks);
    }

    [Fact]
    public void Render_FillsAContentControlByTag()
    {
        WordDocument template = WordDocument.Create();
        Paragraph paragraph = template.Sections[0].AddParagraph();
        paragraph.AppendText("Client: ");
        int start = paragraph.TextLength;
        paragraph.AppendText("placeholder");
        paragraph.AddRange(new InlineContentControl { Tag = "Customer" }, start, paragraph.TextLength - start);

        WordTemplateEngine.Render(template, Sample());

        Assert.Equal("Client: ООО «Ромашка»", paragraph.Text);
    }

    [Fact]
    public void Render_FillsAMergeField()
    {
        WordDocument template = WordDocument.Create();
        Paragraph paragraph = template.Sections[0].AddParagraph("Dear ");
        paragraph.AppendField("MERGEFIELD Customer \\* MERGEFORMAT", "«Customer»");

        WordTemplateEngine.Render(template, Sample());

        Assert.Contains("ООО «Ромашка»", paragraph.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ReportsPlaceholdersTheModelCannotFill()
    {
        WordDocument template = WordDocument.Create();
        template.Sections[0].AddParagraph("{{Customer}} and {{Unknown}}");

        TemplateResult result = WordTemplateEngine.Render(template, Sample());

        Assert.Equal(["Unknown"], result.UnresolvedNames);
    }

    [Fact]
    public async Task Render_PlacesAPictureAndStaysValid()
    {
        WordDocument template = WordDocument.Create();
        template.Sections[0].AddParagraph("Logo: {{Logo}}");

        WordTemplateEngine.Render(template, Sample() with { Logo = ImageData.FromBytes(TestPng) });

        var buffer = new MemoryStream();
        await template.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        buffer.Position = 0;

        using DocumentFormat.OpenXml.Packaging.WordprocessingDocument saved =
            DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(buffer, false);
        var validator = new DocumentFormat.OpenXml.Validation.OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019);
        DocumentFormat.OpenXml.Validation.ValidationErrorInfo[] errors =
            [.. validator.Validate(saved, TestContext.Current.CancellationToken)];
        Assert.Empty(errors);
        Assert.Single(template.Media);
    }

    private static WordDocument RenderConditional(bool hasNotes)
    {
        WordDocument template = WordDocument.Create();
        template.Sections[0].AddParagraph("Always here.");

        var control = new BlockContentControl { Tag = "if:ShowNotes" };
        control.Blocks.Add(new Paragraph("Notes follow."));
        template.Sections[0].Blocks.Add(control);

        WordTemplateEngine.Render(template, Sample() with { HasNotes = hasNotes });
        return template;
    }

    private static Invoice Sample() => new()
    {
        Customer = "ООО «Ромашка»",
        InvoiceNumber = "INV-7",
        Issued = new DateTime(2026, 3, 14),
        Lines =
        [
            new InvoiceLine("Widget", 2, 19.5m),
            new InvoiceLine("Gadget", 1, 4m),
            new InvoiceLine("Doohickey", 3, 7.25m),
        ],
    };

    private static byte[] TestPng { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFUlEQVR4nGP8z8DwnwEJMKEL0FMQAG" +
        "0lAgcqA5wCAAAAAElFTkSuQmCC");
}
