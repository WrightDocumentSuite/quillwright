# Quillwright

A .NET 10 library for Word documents (Apache-2.0). Layered the way `System.Text.Json` is —
primitives, streaming, document model, typed templating — with a paragraph representation
built for the way Word actually stores text.

```csharp
using Quillwright.Model;

var doc = WordDocument.Create();
doc.Sections[0].AddParagraph("Supply agreement", "Heading1");
doc.Sections[0].AddParagraph("Signed today.");
await doc.SaveAsync("agreement.docx");
```

## What it does

- **Edits real documents without breaking them.** Charts, SmartArt, embedded objects, custom
  XML, themes and VBA are carried through byte for byte. Verified against 934 documents
  produced by Word over two decades: every one round-trips with no part lost, identical text,
  and no new schema violation.
- **Data-oriented paragraphs.** A paragraph is one text buffer with runs, hyperlinks,
  bookmarks and comment ranges laid over it as offset ranges. `paragraph.Text` is free, and
  find-and-replace across run boundaries is an ordinary splice rather than a stitching job.
- **Constant-memory streaming** in both directions: `DocxWriter` generates reports without
  building a model, `DocxReader` yields one block at a time.
- **An honest style resolver.** Document defaults, table styles with their conditional
  regions, numbering, the `basedOn` chain and direct formatting, layered in the order
  ISO-29500 specifies, with the exclusive-or semantics that makes bold-inside-bold come out
  unbold.
- **Typed templating with no reflection.** `[WordTemplate]` on a record, and an incremental
  source generator writes the binder. Content controls, `{{placeholders}}` and MERGEFIELDs
  all fill the same way; table rows repeat over collections; regions switch on conditions.
- **Tracked changes you can record.** `using (document.TrackChanges("Ada"))` turns every
  ordinary edit into a marked one: insertions wrapped, deleted text left in place under a
  `w:del`, formatting changes carrying what the formatting was. Accept or reject afterwards to
  get either of the two documents the author was choosing between.
- **A redline from two documents.** `DocumentComparer.Compare(original, revised)` produces the
  original with every difference recorded over it as ordinary tracked changes — word-by-word
  inside changed paragraphs, row marks on changed tables — so accepting them all yields the
  revised text and rejecting them all the original, in Word exactly as here.
- **Documents that assemble.** `contract.Append(annex)` copies another document's content to
  the end, carrying what it leans on — styles with their chains, numbering as fresh instances,
  images, notes, comments with their threading, bookmarks shifted clear — and naming what
  cannot cross a package boundary instead of dangling it.
- **Fields that compute.** `document.UpdateFields()` parses the instruction and works out the
  ones that follow from the document: `=` formulas with the operators and functions of §17.16.3
  (`SUM(ABOVE)` over a table column included), dates, document properties, `IF`, `REF`, with
  the `\#`, `\@` and `\*` picture switches applied. What needs a layout is left dirty for Word.
- **Equations as a tree.** An `m:oMath` is a `MathObject` of fractions, radicals, scripts, sums
  and matrices rather than an opaque blob, so `document.GetText()` reads `x=1/2` and
  find-and-replace reaches inside. Untouched equations still come back byte for byte.
- **Legacy `.doc` both ways.** The compound file, the piece table and the formatting pages are
  read and written directly, with no Office dependency, so a Word 97 document converts to
  `.docx` and back. Floating pictures come out of the OfficeArt drawing layer and chart data
  out of the Microsoft Graph object it is buried in. Files the writer produces are checked by
  opening them in Word itself.
- **PDF, laid out properly.** `document.SaveAsPdf("report.pdf")` paginates the document with
  real font metrics: wrapping and justification, hyphenation from caller-supplied Liang
  patterns with soft hyphens shown only at the break, tabs and their leaders, list counters,
  tables with merged cells and repeating headers, sections with their own page setup and
  balanced columns, footnotes at the foot of the right page, headers and footers with working
  page numbers — `PAGEREF` included, so a table of contents shows where headings actually
  landed — text flowing round floating pictures and text boxes — down both sides, and along a
  wrapping polygon — vertical table cells, Hebrew and shaped Arabic laid right-to-left.
  Optionally tagged, in which case the structure tree passes
  Inkwright's PDF/UA-1 validator. Nothing is shelled out to and no font is guessed at silently —
  every substitution is named in the diagnostics.
- **Markdown with explicit fidelity, both ways.** `document.ToMarkdown()` projects headings,
  formatting, links, bookmarks, tracked-change views, lists, tables, pictures and notes into
  deterministic GitHub Markdown or CommonMark; `MarkdownImporter.Import` reads CommonMark and
  the GitHub extensions back into a real document, onto the same styles the exporter
  recognises, so the two directions are inverses as far as the formats overlap. Every fallback
  is named in diagnostics. See the [export](docs/markdown-export.md) and
  [import](docs/markdown-import.md) guides.
- **HTML both ways, parsed to the standard.** `document.ToHtml()` writes one self-contained
  page for a web preview — semantic elements first, CSS only for what HTML has no element for,
  tables with their merges, real nested lists, footnotes linked both ways, tracked changes as
  `ins`/`del` on request. `HtmlImporter.Import` reads it back, and reads the HTML that editors
  and language models produce: the parser behind it is WHATWG §13.2 itself — all 84 tokenizer
  states, all 21 insertion modes, the adoption agency algorithm, foster parenting, the 2229
  named character references — so `<p>1<b>2<i>3</b>4</i>5` comes out the way a browser makes
  it, which is checked against one. See the [export](docs/html-export.md) and
  [import](docs/html-import.md) guides.
- **Macros you can read.** `document.Macros` decodes the VBA project of a `.docm` or a `.doc`
  into module names, source, the libraries it references and whether it was locked. Useful for
  auditing a document you did not write; a password on the project does not hide any of it.
  Checked against the byte arrays [MS-OVBA] prints for its own algorithms.
- **Async-first I/O** on the .NET 10 asynchronous `ZipArchive` APIs, AOT- and trim-safe.

## Examples

```csharp
// Edit a real document; everything the model does not represent survives
WordDocument report = await WordDocument.LoadAsync("quarterly.docx");
report.Replace("{{Quarter}}", "Q1 2026");
report.Highlight("overdue", f => f with { Bold = true, Highlight = HighlightColor.Yellow });
await report.SaveAsync("quarterly-filled.docx");
```

```csharp
// Build with a cursor
var editor = new DocumentEditor(WordDocument.Create());
editor.WriteHeading("Report", 1)
      .WriteLine("Introduction.")
      .WithFormat(f => f with { Bold = true })
      .WriteLine("Conclusion.");
editor.MoveToFooter().Write("Page ").CurrentParagraph.AppendPageNumber();
```

```csharp
// Stream a hundred thousand rows without a model
await using DocxWriter writer = await DocxWriter.CreateAsync("ledger.docx");
for (int i = 0; i < 100_000; i++)
{
    writer.WriteParagraph($"{i:D6}\tAccount {i % 997:D3}");
    await writer.FlushIfNeededAsync();
}
```

```csharp
// Typed templates, no reflection
[WordTemplate]
public partial record Invoice
{
    public required string Customer { get; init; }
    [TemplateRows("Lines")] public IReadOnlyList<Line> Lines { get; init; } = [];
    [TemplateCondition("ShowTerms")] public bool HasTerms { get; init; }
}

await WordTemplateEngine.RenderAsync("invoice-template.docx", invoice, "invoice.docx");
```

```csharp
// Convert a Word 97 document, and go back the other way
WordDocument legacy = await DocReader.LoadAsync("archive.doc");
await legacy.SaveAsync("archive.docx");
await DocWriter.SaveAsync(legacy, "archive-resaved.doc");
```

```csharp
// Print it: pagination, fonts and all
PdfExportDiagnostics diagnostics = report.SaveAsPdf("quarterly.pdf");
foreach (PdfExportWarning warning in diagnostics)
    Console.WriteLine(warning);
```

```csharp
// Publish the semantic document with deterministic sidecar media
MarkdownDocument markdown = report.ToMarkdown();
await markdown.SaveAsync("quarterly-markdown");
```

## Performance

20 000 paragraphs, .NET 10 on an i7-8700; details and the full method list in
[docs/benchmarks.md](docs/benchmarks.md).

| | Generate | Allocated | Read | Allocated |
| --- | ---: | ---: | ---: | ---: |
| **Quillwright streaming** | **17.0 ms** | **17.8 MB** | **16.6 ms** | **16.8 MB** |
| Quillwright model | 32.7 ms | 18.3 MB | 27.4 ms | 18.3 MB |
| Open XML SDK 3.5 | 46.8 ms | 18.7 MB | 58.1 ms | 15.8 MB |

## Packages

| Package | What it adds |
| --- | --- |
| `Quillwright` | The model, reader/writer, streaming, editing and Markdown export |
| `Quillwright.Templates` | Typed templating and its incremental source generator |
| `Quillwright.Doc` | Reading and writing Word 97-2003 `.doc` files |
| `Quillwright.Pdf` | Rendering to PDF on top of Inkwright: pagination, tables, tagging |

## Documentation

- [Getting started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [Conformance matrix](docs/conformance.md) — what is read, written, preserved and evaluated,
  format by format, with the boundary of each
- [The document model](docs/model.md)
- [Styles and numbering](docs/styles.md)
- [Streaming](docs/streaming.md)
- [Editing, search and revisions](docs/editing.md)
- [Fields](docs/fields.md)
- [Equations](docs/math.md)
- [Digital signatures](docs/signatures.md)
- [Templates](docs/templates.md)
- [Legacy .doc import](docs/doc-import.md)
- [Legacy .doc export](docs/doc-export.md)
- [Rendering to PDF](docs/pdf-export.md)
- [Exporting to Markdown](docs/markdown-export.md)
- [Importing Markdown](docs/markdown-import.md)
- [Exporting HTML](docs/html-export.md)
- [Importing HTML](docs/html-import.md)
- [Macros](docs/macros.md)
- [Benchmarks](docs/benchmarks.md)
- [Running the tests](docs/testing.md)

## Building

Requires the .NET 10 SDK.

```bash
dotnet build Quillwright.slnx
dotnet test Quillwright.slnx
dotnet run -c Release --project benchmarks/Quillwright.Benchmarks -- --filter *
dotnet run --project samples/Quillwright.Samples
```

A clone runs green. Some tests skip: they read a corpus of real Word documents that belongs to
other projects and is not shipped here, and each skip names the collection and where to get it.
[docs/testing.md](docs/testing.md) has the details.

## Deliberate limits

The core has no layout engine, so `AutoFit` is not resolved and a field that needs a layout is
left for Word to recompute; pagination lives in `Quillwright.Pdf`, which computes the page
fields — `PAGEREF` included — against its own layout while rendering, without touching what
the document stores. SmartArt survives a round trip but has no API; embedded objects, web
extensions and macros are read but never authored, and a chart is read and its data replaced
(`SetChartData`) but never created. A digital signature is verified — the value against the
signer's key, each covered part against its digest — and `DocumentSigner` signs a saved
package the same way Word does; whether to trust a certificate stays a policy the caller
supplies, and no XAdES qualifying properties are written. Encryption is
read four ways and written one: a package is locked with AES-256 and a `.doc` with the RC4 the
format has. RTF and ODT are not read or written; Markdown and HTML are, both ways. Writing `.doc`
is a conversion rather than a round trip:
the format has no revisions and no content controls, so those are written as their accepted or
unwrapped form, with a warning naming what changed. Two formats that never appear inside a
document are read standalone and never written: a co-authoring lock file ([MS-WORDLFF]), whose
whole structure is read but whose protocol is not spoken, and an Office add-in manifest
([MS-OWEMXML]), as the metadata its two base namespaces share.

Which of these is read, written, preserved or evaluated — and where each one stops — is in
[conformance.md](docs/conformance.md). The reasoning behind the boundaries is in
[architecture.md](docs/architecture.md), and what §17 says against what this reads is in
[wordprocessingml-coverage.md](docs/wordprocessingml-coverage.md).

## Licence

Apache License 2.0 — see [LICENSE](LICENSE). No code from other projects is vendored here;
what is implemented from published specifications, and what is deliberately left out because
it carries someone else's licence, is listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
