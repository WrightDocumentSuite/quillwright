# Templates

```bash
dotnet add package Quillwright.Templates
```

A template is an ordinary Word document with the places to fill marked. The model is an
ordinary C# type. An incremental source generator writes the binder that connects them, so
filling a document uses no reflection and works under Native AOT.

## The model

```csharp
using Quillwright.Templates;

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

    [TemplateCondition("ShowTerms")]
    public bool HasTerms { get; init; }

    [TemplateImage(WidthCentimeters = 3)]
    public ImageData? Logo { get; init; }
}
```

The type must be `partial`. Every public property and field takes part unless marked
`[TemplateIgnore]`; the role is inferred from the type — `bool` is a condition, `ImageData` is
a picture, everything else is text — and can be stated explicitly with an attribute.

## Marking the template

Three conventions are supported, because documents in the wild use all three, and a template
can mix them.

| Convention | Looks like | Best for |
| --- | --- | --- |
| Placeholder | `{{Customer}}` typed into the text | quick templates |
| Content control | a control tagged `Customer` | templates that authors edit in Word |
| Merge field | a `MERGEFIELD Customer` field | documents left over from a mail merge |

A placeholder works even when Word has split it across runs, which it usually has.

**Repeated rows.** A table row whose placeholders use dot notation with a collection name repeats
once per item:

| Description | Qty | Amount |
| --- | --- | --- |
| `{{Lines.Description}}` | `{{Lines.Quantity}}` | `{{Lines.Amount}}` |

**Repeated blocks.** A block-level content control tagged `rows:Lines` repeats everything
inside it per item.

**Conditions.** A block-level content control tagged `if:ShowTerms` is kept when the condition
is true and dropped when it is false.

## Filling

```csharp
TemplateResult result = await WordTemplateEngine.RenderAsync("invoice-template.docx", invoice, "invoice.docx");

Console.WriteLine($"{result.ValuesFilled} filled, {result.RegionsRepeated} rows, {result.RegionsRemoved} removed");
foreach (string name in result.UnresolvedNames)
    Console.WriteLine($"the template asks for {name}, which the model does not have");
```

To work on a document already in memory:

```csharp
WordDocument template = await WordDocument.LoadAsync(path);
WordTemplateEngine.Render(template, invoice);
```

Rendering happens in three passes, because each changes what the next one sees: repeated
regions expand first, since they create paragraphs; conditions resolve next, since removing a
region removes the anchors inside it; values fill last. Every pass works on offsets, so a
placeholder split across runs is filled without the caller ever knowing it was split, and a
placeholder inside a hyperlink keeps the hyperlink.

Placeholders the model cannot answer are left in place and listed in `UnresolvedNames`, so a
template mistake shows up as a report rather than as blank space in the output.

## Without the generator

The engine takes a binder directly, which is useful for data that is not a compile-time type:

```csharp
WordTemplateEngine.Render(document, model, myBinder);
```

`ITemplateBinder` has four lookups — text, rows, condition, image — each taking the model as
`object` and a name.
