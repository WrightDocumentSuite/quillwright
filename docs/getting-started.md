# Getting started

```bash
dotnet add package Quillwright
```

Everything is asynchronous at the edges — loading, saving — and synchronous in between, so
building a document is ordinary code.

## Create a document

```csharp
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

var document = WordDocument.Create();
Section section = document.Sections[0];

section.AddParagraph("Supply agreement", "Heading1");
section.AddParagraph("This agreement is made between the parties named below.");

Paragraph signature = section.AddParagraph();
signature.AppendText("Signed on ", RunFormat.Default with { Italic = true });
signature.AppendDate();

await document.SaveAsync("agreement.docx");
```

A new document has one section with A4 pages and the styles Word expects. Asking for a
built-in style by name creates it: `AddParagraph(text, "Heading1")` brings the definition of
Heading 1 into the file, along with the styles it is based on.

## Open, change, save

```csharp
WordDocument document = await WordDocument.LoadAsync("contract.docx");

document.Replace("{{Client}}", "Romashka LLC");
document.Paragraphs.First().Format = document.Paragraphs.First().Format with
{
    Alignment = ParagraphAlignment.Center,
};

await document.SaveAsync("contract-filled.docx");
```

Loading reads the package to the end and closes it, so there is no open handle between calls.
Anything the model does not represent — charts, embedded objects, custom XML, macros — is held
aside and written back on save.

Every load also has a finite resource budget. For uploads or other caller-controlled files,
set the limits for the service explicitly and handle `DocumentLoadLimitException` separately
from malformed packages:

```csharp
var options = new LoadOptions
{
    Budget = DocumentLoadBudget.Default with { MaxInputBytes = 32 * 1024 * 1024 },
};
WordDocument uploaded = await WordDocument.LoadAsync(path, options);
```

The complete cross-format policy is in
[loading-untrusted-input.md](loading-untrusted-input.md).

## Formatting

Formats are immutable records where `null` means "inherit":

```csharp
var heading = RunFormat.Default with
{
    Bold = true,
    Size = Length.FromPoints(16),
    Color = WordColor.FromRgb(0x2F5496),
    FontAscii = "Georgia",
};

paragraph.AppendText("Terms", heading);
paragraph.ApplyFormat(0, 5, f => f with { Underline = UnderlineStyle.Single });
```

`Length` is one type for every unit the format uses, so the unit never appears in a signature:

```csharp
Length.FromPoints(12)        // font sizes, spacing
Length.FromCentimeters(2.5)  // indents, page geometry
Length.FromInches(1)
Length.FromEighthPoints(4)   // border widths
Length.FromEmu(914400)       // drawings
```

## Tables

```csharp
Table table = section.AddTable(rows: 3, columns: 3);
table.Format = table.Format with { StyleId = "TableGrid" };
document.Styles.GetOrAdd("TableGrid", StyleKind.Table);

table[0, 0].SetText("Item", RunFormat.Default with { Bold = true });
table.Rows[0].Format = table.Rows[0].Format with { IsHeader = true };
table.AddRow("Widget", "12", "19.50");
table.MergeCells(firstRow: 1, firstColumn: 0, rowCount: 2, columnCount: 1);
```

A cell holds blocks, so a table can contain a table.

## Headers, footers and page setup

```csharp
section.Properties.Orientation = PageOrientation.Landscape;
section.Properties.Margins.Left = Length.FromCentimeters(3);

Paragraph footer = section.Footers.GetOrCreate().AddParagraph(string.Empty, "Footer");
footer.AppendText("Page ");
footer.AppendPageNumber();
```

`GetOrCreate` makes the part and wires up the relationship. Asking for
`HeaderFooterKind.First` also sets `DifferentFirstPage`, because a first-page header that is
never shown is not what the caller meant.

## Where to go next

- [The document model](model.md) — paragraphs, runs, anchors and how they move under editing
- [Styles and numbering](styles.md) — the resolver and the lists
- [Editing, search and revisions](editing.md) — the cursor, find and replace, tracked changes
- [Streaming](streaming.md) — generating and reading without a model
- [Templates](templates.md) — filling documents from typed models
