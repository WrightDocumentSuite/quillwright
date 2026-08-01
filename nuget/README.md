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
  ISO-29500 specifies.
- **Typed templating with no reflection.** `[WordTemplate]` on a record, and an incremental
  source generator writes the binder.
- **Tracked changes you can record**, and a redline from two documents:
  `DocumentComparer.Compare(original, revised)` records every difference as an ordinary
  tracked change, so accepting them all yields the revised text and rejecting them all the
  original.
- **Documents that assemble.** `contract.Append(annex)` copies another document's content in,
  carrying the styles, numbering, images, notes and comments it leans on.
- **Fields that compute**, equations as a tree, legacy `.doc` both ways, digital signatures
  verified and written, and macros you can read.
- **PDF, laid out properly.** `document.SaveAsPdf("report.pdf")` paginates with real font
  metrics: wrapping and justification, hyphenation, tables with merged cells and repeating
  headers, footnotes, headers and footers with working page numbers, text flowing round
  floating pictures, right-to-left scripts. Optionally tagged for PDF/UA.
- **Markdown and HTML, both ways.** Deterministic export and an import that inverts it; the
  HTML parser implements the WHATWG parsing algorithm rather than approximating it.
- **Async-first I/O** on the .NET 10 asynchronous `ZipArchive` APIs, AOT- and trim-safe.

## Packages

| Package | What it adds |
| --- | --- |
| [`Quillwright`](https://www.nuget.org/packages/Quillwright) | The model, reader/writer, streaming, editing, comparison, Markdown and HTML |
| [`Quillwright.Templates`](https://www.nuget.org/packages/Quillwright.Templates) | Typed templating and its incremental source generator |
| [`Quillwright.Doc`](https://www.nuget.org/packages/Quillwright.Doc) | Reading and writing Word 97-2003 `.doc` files |
| [`Quillwright.Pdf`](https://www.nuget.org/packages/Quillwright.Pdf) | Rendering to PDF on top of Inkwright: pagination, tables, tagging |

## Documentation

The guides live in the repository, which is also where the conformance matrix says what is
read, written, preserved and evaluated, format by format, with the boundary of each:

- [Getting started](https://github.com/WrightDocumentSuite/quillwright/blob/main/docs/getting-started.md)
- [Architecture](https://github.com/WrightDocumentSuite/quillwright/blob/main/docs/architecture.md)
- [Conformance matrix](https://github.com/WrightDocumentSuite/quillwright/blob/main/docs/conformance.md)
- [The document model](https://github.com/WrightDocumentSuite/quillwright/blob/main/docs/model.md)
- [Editing, search and revisions](https://github.com/WrightDocumentSuite/quillwright/blob/main/docs/editing.md)
- [Rendering to PDF](https://github.com/WrightDocumentSuite/quillwright/blob/main/docs/pdf-export.md)
- [Templates](https://github.com/WrightDocumentSuite/quillwright/blob/main/docs/templates.md)

## Deliberate limits

The core has no layout engine, so `AutoFit` is not resolved and a field that needs a layout is
left for Word to recompute; pagination lives in `Quillwright.Pdf`. SmartArt survives a round
trip but has no API; embedded objects, web extensions and macros are read but never authored,
and a chart is read and its data replaced but never created. Encryption is read four ways and
written one. RTF and ODT are not read or written; Markdown and HTML are, both ways. Writing
`.doc` is a conversion rather than a round trip, with a warning naming whatever changed.

Which of these is read, written, preserved or evaluated — and where each one stops — is in the
[conformance matrix](https://github.com/WrightDocumentSuite/quillwright/blob/main/docs/conformance.md).

## Licence

Apache License 2.0 — see
[LICENSE](https://github.com/WrightDocumentSuite/quillwright/blob/main/LICENSE) and
[THIRD-PARTY-NOTICES.md](https://github.com/WrightDocumentSuite/quillwright/blob/main/THIRD-PARTY-NOTICES.md).
