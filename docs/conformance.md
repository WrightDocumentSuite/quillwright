# Conformance matrix

What this library does with each format it touches, split four ways, with the boundary of each
named rather than implied.

- **Read** — the bytes are decoded into the object model.
- **Write** — the model is encoded back into that format.
- **Preserve** — bytes the model does not interpret survive a load and save unchanged.
- **Evaluate** — something is computed or checked rather than merely carried: a field value, a
  digest, a page of layout.

A cell says *subset* wherever the support is real but partial; the last column says which part.
Nothing here is a claim of full conformance with a specification, and where a row is a subset
the linked document says exactly which elements or records are on which side of the line.

| Format | Read | Write | Preserve | Evaluate | Known boundary |
| --- | :-: | :-: | :-: | :-: | --- |
| **WordprocessingML** §17 (`.docx`, `.docm`) | yes | yes | yes | — | 389 of the 547 elements are named in the reader; the other 158 are carried whole. [Coverage](wordprocessingml-coverage.md) |
| **Styles and numbering** §17.7, §17.9 | yes | yes | yes | yes | Resolution follows the standard's layering; there is no layout, so `AutoFit` is unresolved. [Styles](styles.md) |
| **Fields** §17.16 | yes | yes | yes | subset | Deterministic fields and `=` formulas are computed; `PAGE`, `PAGEREF`, `TOC`, `INDEX` and the mail-merge family keep their cached result and are marked dirty. A cell reference names a column of the table's grid, so a span is addressed and counted the way §17.16.3.5 describes. [Fields](fields.md) |
| **Office Math (OMML)** §22.1 | subset | subset | yes | — | All 20 objects and their structure are a tree — 75 of the 124 elements — and 4 more are carried verbatim through a regenerated equation. The 45 that say how a formula is spaced and broken stay as markup, and are lost only if the tree is edited. [Equations](math.md) |
| **Markup Compatibility** ISO/IEC 29500-3 | subset | yes | yes | yes | An `mc:AlternateContent` is resolved by §9.3 at both levels using an explicit application configuration: the selected branch is modelled when it is one picture in a run, or whole blocks in a body, and ignorable extension children do not prevent selection. Every branch is preserved either way. Outside that wrapper, `mc:Ignorable`, `mc:ProcessContent` and the attribute forms are carried rather than acted on. |
| **Digital signatures** ECMA-376 part 2 cl. 10 | yes | yes | yes | yes | The value is verified against the signer's key over canonicalised `SignedInfo`, each covered part against its digest, and the relationships transform of 10.6 is performed. `DocumentSigner` signs a saved package with RSA or ECDSA — the manifest, the transform, the `SignatureTime` and the `SignatureInfoV1` Word shows — and a second signature leaves the first standing. No XAdES is written. Certificate trust is a separate question the caller's policy answers. [Signatures](signatures.md) |
| **Encryption** [MS-OFFCRYPTO] | yes | subset | — | — | Four schemes are read; agile package encryption honours AES-CBC/CFB parameters, selects the password key encryptor by namespace and verifies `dataIntegrity`. A package is written locked with agile AES-256-CBC and a `.doc` with the RC4 the binary format has. |
| **VBA** [MS-OVBA] | yes | no | yes | — | Modules, source, references and protection state are decoded; nothing authors a project. [Macros](macros.md) |
| **User forms** [MS-OFORMS] | subset | no | yes | — | The layout beside a form module: every control site with its name, kind, place, size, tab order, tooltip, binding and group, and the captions and values of the controls that carry them, descending into frames, pages and multi-pages. The pictures, fonts, colours and list contents of a control are stepped over rather than surfaced, and nothing authors a form. [Macros](macros.md) |
| **Embedded objects** [MS-OLEDS], [MS-OLEPS] | yes | no | yes | — | Program, display name and bytes; the object's own format is not interpreted. |
| **Word 97-2003** `.doc` [MS-DOC] | yes | yes | no | — | Writing is a conversion: revisions, content controls and equations have no home in the format and are written flattened, each with a warning. Reading is lossy in the other direction, likewise warned. [Import](doc-import.md), [export](doc-export.md) |
| **OfficeArt** [MS-ODRAW] | subset | subset | — | — | Floating pictures with their anchor and wrapping, the seven BLIP types including PICT, and what a shape says about how it looks: its preset kind, its fill, its line, its rotation and its lettering. Geometry of its own, group transforms, shadows and effects are not modelled — the reference corpus contains none of them, which is [written down](doc-import.md). |
| **Charts** §21.2 | subset | subset | yes | — | The kind, the title and the cached series of a chart part, and the frame in the body that draws it. Nothing about how the chart is styled is read, and nothing authors one — but `SetChartData` rewrites the data of an existing chart, series for series, as literals: the names, categories, values and bubble sizes change, the look does not, and the formula that pointed into the embedded workbook is gone from what was rewritten. Everything else in the part is copied through whole. |
| **Microsoft Graph charts** [MS-OGRAPH] | subset | no | yes | — | The data sheet, the series drawn from it with their bubble sizes, the chart groups that give a combined chart two kinds, and records continued across a `Continue`. Trendlines and error bars are recognised and left out of the series. Nothing about how the chart looks is read; the embedded object is preserved whole, so nothing is lost. |
| **Web extensions** [MS-OWEXML] | yes | no | yes | — | The in-document `taskpanes.xml` and `webextensionN.xml` parts, as a typed metadata view. No authoring. |
| **Add-in manifests** [MS-OWEMXML] | subset | no | — | — | A base-manifest metadata subset, read standalone: the elements the 1.0 and 1.1 namespaces share. The three add-in vocabularies are not modelled and `VersionOverrides` is handed back as markup, uninterpreted. |
| **Co-authoring lock files** [MS-WORDLFF] | yes | no | — | — | A standalone structural reader for the whole of `CT_CALocks`. No stream authoring, no [MS-FSSHTTP] transport, and a `Sync` request is reported rather than applied to a document. |
| **Command tables** [MS-CTDOC] | detect | no | no | — | `fcCmds`/`lcbCmds` is noticed and reported as a loss; the `Tcg` itself is not decoded and no later format has anywhere to put it. |
| **PDF** | — | subset | — | yes | A renderer, not a converter: pagination, tables, notes, wrapping, equations, charts and tagging. JPEG and PNG travel untouched; bitmap, GIF, TIFF and the bitmap inside a metafile are decoded and re-encoded. [PDF export](pdf-export.md) |
| **Markdown** | subset | subset | — | — | Both directions are semantic projections over a shared subset, not general inverses. Import reads CommonMark and the GitHub table, strikethrough and task-list extensions; raw HTML stays text, so HTML fallbacks emitted for notes or non-GFM tables are not reconstructed as those document constructs. Export diagnostics name each approximation. [Import](markdown-import.md), [export](markdown-export.md) |
| **HTML** [WHATWG] | yes | subset | — | — | Parsing is the standard's own algorithm: all 84 tokenizer states, all 21 insertion modes, the adoption agency algorithm, foster parenting and the 2229 named character references — checked case by case against a browser's parser. What the *importer* then maps into the document model is a subset, named in its diagnostics. Export is a semantic projection, but the importer does not yet reconstruct every exported construct, including notes. [Import](html-import.md), [export](html-export.md) |
| **RTF 1.9.1** | subset | subset | no | — | Semantic conversion of text, Unicode/code pages, resources, direct character and paragraph formatting, tabs, breaks and sections. Styles, lists, tables, notes, headers/footers and drawing destinations are not converted yet; skipped or flattened content is named in diagnostics. [Import](rtf-import.md), [export](rtf-export.md) |
| **ODT** | no | no | — | — | Not supported in either direction. |

## Formats that are not in a document

Two of the rows above read files that never appear inside a `.docx` or a `.doc`, and are
therefore reached through a reader of their own rather than through `WordDocument`:

```csharp
// Who is holding which paragraph of a shared document, and why it will not open for editing
CoAuthoringLocks? file = CoAuthoringLockFile.ReadAll(await File.ReadAllBytesAsync("locks.bin"));
foreach (CoAuthoringLock held in file?.Effective ?? [])
    Console.WriteLine($"{held.OwnerName} holds {held.Paragraphs.Count} paragraph(s)");

// What an Office add-in claims to be, from the manifest a catalogue distributes
OfficeAddInManifest? manifest = OfficeAddInManifestReader.Read(await File.ReadAllBytesAsync("addin.xml"));
Console.WriteLine($"{manifest?.Kind}: {manifest?.DisplayName?.For("en-US")}");
foreach (OfficeAddInSourceLocation page in manifest?.SourceLocations ?? [])
    Console.WriteLine($"  {page.Context} loads {page.Url.DefaultValue}");
```

Neither reader opens a network connection, resolves an external entity, or follows any address
it reads. A lock file cannot be found from a document and an add-in manifest cannot be
reconstructed from one, so neither is a property of `WordDocument`.

## Where the deliberate departures are written down

Coverage of §17 element by element is in
[wordprocessingml-coverage.md](wordprocessingml-coverage.md), which also lists the four places
this library knowingly departs from the standard because Word does, each checked against
[MS-OI29500]. The architectural reasons behind the boundaries above — no layout engine in the
core, preservation over interpretation — are in [architecture.md](architecture.md).
