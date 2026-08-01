using Quillwright.Model;
using Quillwright.Templates;

namespace Quillwright.Templates.Tests;

[WordTemplate]
public partial record InvoiceLine(
    [property: TemplateField("Description")] string Text,
    int Quantity,
    [property: TemplateField(Format = "N2")] decimal Amount);

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

    [TemplateCondition("ShowNotes")]
    public bool HasNotes { get; init; }

    [TemplateImage(WidthCentimeters = 2, HeightCentimeters = 1)]
    public ImageData? Logo { get; init; }
}
