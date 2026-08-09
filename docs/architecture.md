# Architecture

Quillwright is layered the way `System.Text.Json` is: a primitive layer that knows units and
packaging, a streaming layer that reads and writes without holding a document in memory, a
document model built on top of it, and typed templating built on top of that. Every layer is
public and usable on its own.

```
L3  Quillwright.Templates   ITemplateBinder + incremental source generator (no reflection, AOT)
    Quillwright.Doc         Word 97-2003 both ways: compound file, piece table, formatting pages
    Quillwright.Pdf         Pagination and rendering to PDF, on top of Inkwright
    Quillwright.Rtf         Semantic RTF 1.9.1 import/export through the document model
L2  Model                   WordDocument / Section / Paragraph / Table, style resolver, editing
    Markdown, HTML          Pure semantic projections both ways + media, no layout or dependency
L1  Streaming               DocxReader (pull, one block at a time) / DocxWriter (forward-only UTF-8)
L0  Primitives + OPC        Length, WordColor, OpcPackage, CompoundFile, Utf8XmlWriter, RawXml
```

The compound file reader sits at L0 rather than with the legacy package, because two formats
need it: a `.doc` is one, and so is the VBA project inside a `.docm`. Reading macros therefore
belongs to the core, and `Quillwright.Doc` points the same reader at a storage instead of a
whole file.

`Quillwright.Templates`, `Quillwright.Doc`, `Quillwright.Pdf` and `Quillwright.Rtf` are separate
packages rather than parts of the core, so a caller who only reads and writes DOCX documents
pays for none of them.
The PDF package is the only one with a dependency outside this repository — Inkwright, which
writes the file it lays out — and the dependency runs one way: the core knows nothing about it.

Markdown export stays in the core because it is a projection of the model rather than a layout
engine. `ToMarkdown` walks the same paragraph buffer, runs, objects, marks and ranges described
below, resolves styles without changing them, and returns text plus encoded sidecar images before
the optional filesystem layer runs. List counting and number formatting are internal core services
shared with the PDF renderer so starts, overrides and restarts cannot acquire exporter-specific
semantics. See [markdown-export.md](markdown-export.md).

## The paragraph is the design

Everything else follows from one decision: a paragraph stores its text as a single buffer and
describes everything else as offsets into it.

```
text     "See the terms and conditions before signing."
runs     [0,8) plain   [8,22) bold   [22,44) plain
ranges   hyperlink over [8,22)
marks    bookmarkStart "terms" at 8, bookmarkEnd at 22
objects  (none)
```

WordprocessingML splits text into runs wherever formatting changes, and Word splits it further
at almost every edit, so a sentence typed in one go commonly arrives as a dozen runs with the
same properties. Storing the text contiguously and the formatting as ranges over it means:

- `paragraph.Text` costs nothing; there is no run stitching anywhere in the library.
- Search and replace across run boundaries is a splice, not a special case. Replacing
  `{{Client}}` works whether Word split it into one run or five.
- Formatting costs one small struct per run instead of an object graph. A run is
  `(start, length, format, kind, attributes)`, and equal formats are interned on read.
- A hyperlink, a tracked insertion or a content control is a range, so text can be edited
  across its edges and the wrapper follows.

Content that is neither text nor formatting occupies exactly one character each, so every
offset in the paragraph stays meaningful: a tab is `\t`, a break is `\n`, and everything else
— a picture, a footnote reference, a field boundary — is `U+FFFC` with an entry in the object
list. Marks such as bookmarks are zero-width and sit between characters, which is exactly how
the schema models them.

Writing is the inverse. [`ParagraphEmitter`](../src/Quillwright/Formats/ParagraphEmitter.cs)
walks the offsets in ascending order, closing wrappers that end, emitting marks that sit
there, opening wrappers that start, and writing run content up to the next boundary. A stack
of open wrappers re-creates the nesting the schema requires from a flat set of ranges.

## Sections

WordprocessingML has no section element. The body is a flat list of blocks, and a section ends
at the paragraph whose properties carry a `w:sectPr`, with the last section's properties at the
end of the body. The loader splits that flat list into `Section` objects and the writer
flattens it back, so the model can present the structure authors think in while the file keeps
the shape the schema demands.

## Formats are immutable records with named slots

`RunFormat`, `ParagraphFormat`, `TableFormat` and the rest are records where `null` means
"inherit" and a value means "override here". Record equality gives free interning: the reader
canonicalises every format it sees, so a document with a hundred thousand runs holds a handful
of format instances.

Properties the model does not interpret are **not** collected into one bag. `CT_RPr` and
`CT_PPr` declare their children in a fixed order, and appending unknown elements at the end
produces a file that Word repairs. Each such element instead gets a named slot —
`EffectXml`, `FitTextXml`, `EmphasisXml`, `EastAsianLayoutXml`, `MarkRevisionXml`,
`ChangeXml` — written back exactly where the schema wants it.

## Preserving what is not understood

Saving is copy-on-write at the package level. Parts the model rebuilds are written from the
model; every other part of a loaded package is copied through byte for byte with its
relationships and its content type. Inside `document.xml`, markup the model does not interpret
— VML pictures, OLE objects, ink annotations — lives in the model as a `RawInline`, `RawMark`,
`RawRange` or `RawBlock` and is written back in place.

An equation is the same bargain drawn one level finer. `MathObject` models the structures of
§22.1 that carry meaning — fractions, radicals, scripts, sums, delimiters, matrices — and keeps
every other element as a `RawMath` holding its own bytes. The whole equation also keeps the
markup it arrived as, so one that nobody edited is written back byte for byte and the spacing,
breaking and font settings the tree has no room for survive; `Invalidate()` gives that up in
exchange for the edit showing. See [math.md](math.md).

`mc:AlternateContent` is the one wrapper that gets opened rather than kept whole.
[`MceReader`](../src/Quillwright/Formats/MceReader.cs) picks the branch a reader of this
vocabulary sees — the first `mc:Choice` whose `Requires` prefixes all name vocabularies for
which this library has semantic readers, otherwise the `mc:Fallback` (ISO/IEC 29500-3 §9.3).
That application configuration is an explicit list, not a URI-prefix test: preserving an unknown
Office extension does not make its meaning understood. Ignorable extension children beside the
branches or inside one are omitted while choosing and modelling, as Part 3 requires, while their
original bytes remain in the wrapper. The selected content is modelled, so a picture Word wrapped
for compatibility is a `Picture` like any other rather than an opaque fragment invisible to the
media API, and a paragraph Word wrapped is a paragraph whose words can be reached by `GetText`
and find-and-replace. The markup around the chosen branch is sliced out of the original bytes rather than
rebuilt, so the alternative an older reader falls back to comes back byte for byte even after the
chosen branch has been edited.

Inside a run the branch has to be one `w:drawing` holding one picture, and anything less tidy
stays a `RawInline`. "One picture" counts two arrangements: a `pic:pic`, and a single `wps:wsp`
whose shape properties are filled with one blip — Word writes an image either way, and a reader
that knew only the first would leave half of them invisible. A shape that also carries text is a text
box and is left as one. At block level the branch can hold anything block-level, and becomes an
[`AlternateContentBlock`](../src/Quillwright/Model/AlternateContentBlock.cs) — a block that is
also a container, the same shape as a content control; a branch this reader cannot parse leaves
the whole element a `RawBlock`, as before.

Three rules make this actually work rather than nearly work:

1. **Relationship ids are part-scoped and never renumbered.** Preserved markup points at its
   targets by id, so a chart would silently repoint at a footnote. While reading, every image,
   hyperlink, chart frame and embedded-object reference resolves against the relationships of
   the part whose markup contains it. While writing, the document, each header and footer, and
   the notes and comments parts allocate within independent namespaces of ids. A header image
   and a footer hyperlink therefore never acquire misleading relationships on `document.xml`.
   New references get ids the allocator has verified are free in that source part.
2. **Part paths come from the relationships**, not from the conventional names. A package is
   free to put `styles.xml` anywhere its relationship points, and Strict producers often do.
   A relationship's `Target` is a URI rather than a part name, so it is unescaped on the way in
   and escaped on the way out; a part is then looked for under the name it was given and under
   the percent-encoded spelling ECMA-376 part 2 §7.3.4 asks a ZIP item name to use, because
   producers disagree about which of the two they write.
3. **A Strict package stays Strict.** Converting only the parts the model regenerates would
   leave the copied ones speaking `purl.oclc.org` inside a file whose main part had turned
   Transitional. Relationship types are therefore stored as read, compared in canonical form,
   and written back in the original spelling; generated parts inherit the source's own
   namespace declarations. The vocabulary follows the namespace: Strict renamed the direction
   words that assume a left-to-right page, so a regenerated part writes `w:ind w:start`,
   `w:tcBorders/w:start` and `w:jc w:val="start"` where a Transitional one writes `left`. The
   rename is not uniform — `CT_PBdr` and `CT_PageMar` kept theirs — so it is applied type by
   type; [wordprocessingml-coverage.md](wordprocessingml-coverage.md) lists which.

That last point has a subtler companion: a part whose children are preserved verbatim reuses
its own root start tag rather than a fixed namespace block, because Word bound the `w14`
prefix to a 2007 beta namespace before it meant the 2010 one, and re-declaring the prefix
would move every preserved element into a namespace `mc:Ignorable` no longer covers.

## Diagnostics instead of silent failure

Loading is recovery-oriented. A dangling relationship, an unresolvable image, an attribute that
does not parse — each becomes a [`DocumentWarning`](../src/Quillwright/Diagnostics/DocumentWarning.cs)
with a code and the part it came from, collected in `WordDocument.LoadDiagnostics` and passed
to `LoadOptions.OnWarning`. `DocxFormatException` is thrown only when a package is broken
beyond recovery: not a zip, no main document part, XML that no longer parses.

Resource refusal is a third, deliberate outcome. `LoadOptions.Budget` is checked before input
and part allocations and while XML is counted; `DocumentLoadLimitException` names the exact
ceiling and observed value. The same `DocumentLoadBudget` is used by DOC, HTML, Markdown and RTF
imports so an upload service can apply one policy. See
[loading-untrusted-input.md](loading-untrusted-input.md).

OPC media and embedded-object ceilings follow part semantics, not ZIP folder conventions.
Image/audio/video content types and their relationship roles identify media; `oleObject` and
`package` relationships identify embeddings even when a valid producer stores the target away
from `/media/` or `/embeddings/`. The conventional directories remain a defensive fallback for
malformed or underspecified packages.

A file that is intact but unreadable gets a reason rather than that verdict. An encrypted OOXML
document is not a zip at all — it is a compound file holding the package as an
`EncryptedPackage` stream beside the `EncryptionInfo` that unlocks it ([MS-OFFCRYPTO] 2.3.4.4
and 2.3.4.5) — so the first bytes are checked before the zip reader sees them. Given
`LoadOptions.Password` the package is decrypted and read as normal, and without one, or with
the wrong one, both that and the `fEncrypted` flag of a `.doc` raise
`EncryptedDocumentException`. One `catch` covers either format, and because it derives from
`DocxFormatException`, code that already refuses unreadable packages keeps working. A legacy
`.doc` handed to `WordDocument.LoadAsync` is named as one and pointed at `Quillwright.Doc`.

## Encryption

All four schemes are read. For a package: the older one, which hashes the password fifty
thousand times and encrypts with AES in one block-independent pass, and the newer one, which
describes itself in a small XML document and encrypts in chained four-kilobyte segments
([MS-OFFCRYPTO] 2.3.4.5-2.3.4.15). For a `.doc`: both RC4 schemes (2.3.5 and 2.3.6), decrypting
the document, table and data streams in 512-byte blocks, and the XOR obfuscation that came
before them (2.3.7), which is not encryption at all — a sixteen-byte pad exclusive-ored over
the streams, with the rule that a byte stays as it is when it or the result would be zero.

For agile package encryption, password and certificate key encryptors are distinguished by
namespace; a certificate entry can therefore coexist with the one unlocked by
`LoadOptions.Password`. AES-CBC and AES-CFB with its required eight-bit feedback window are
read according to `cipherChaining`. Other declared ciphers are rejected explicitly instead of
being guessed as AES. When `dataIntegrity` is present, its encrypted HMAC key and value are
unlocked and verified over the complete `EncryptedPackage` stream, including its length prefix,
before any package bytes are accepted.

Locking a document is `SaveOptions.Password` for a package and `DocWriteOptions.Password` for a
`.doc`:

```csharp
await document.SaveAsync("secret.docx", new SaveOptions { Password = "correct horse" });
```

A package is locked with the newer scheme only — AES-256 in CBC, SHA-512, a hundred thousand
iterations, and the integrity check of 2.3.4.14 that Word verifies before it even asks for the
password. The result is not a package but a compound file holding one, which is what Word
writes and what it expects handed back. A `.doc` is locked with RC4 CryptoAPI, because that is
what the format has; it is weak, and a caller who wants encryption worth the name should be
saving `.docx`. The two older schemes and the obfuscation are read and never written.

The writers are checked against a test-only encryptor written from [MS-OFFCRYPTO] independently
of them, so that a mistake shared by the library's two halves cannot cancel itself out.

`w:documentProtection` is modelled rather than preserved, so the restrictions an author asked
for can be read, a password checked against the stored hash with `IsPassword`, and one stored
with `SetPassword` — SHA-512 over a fresh 16-byte salt, iterated the hundred thousand times
Word uses, in the salt-then-password order §17.15.1.29 prescribes. The standard is clear that
this is not a security feature, and neither is reading or writing it: the element asks a
consumer to honour a restriction, it does not enforce one.

## Beside the document

A shared document has a file next to it that says who is editing which paragraphs, so that a
co-author is told why a region will not open rather than left to discover it
([MS-WORDLFF]). It travels over a protocol this library has nothing to do with and is not
stored in the document, so `CoAuthoringLockFile.Read` is a standalone helper: hand it the
bytes — deflated XML behind an eight-byte signature — and it says who holds what.

## .NET 10 and C# 14 features used

| Feature | Where |
| --- | --- |
| Async `ZipArchive` API | `OpcPackage`, all package I/O |
| u8 literals + `IUtf8SpanFormattable` | `Utf8XmlWriter`, every element the writer emits |
| `SearchValues` | XML escaping |
| `params ReadOnlySpan<T>` | Attribute capture, `Table.AddRow` |
| `CollectionsMarshal.AsSpan` | In-place run editing without copying |
| Static abstract interface members | `ITemplateModel.TemplateBinder` |
| Incremental source generators | `Quillwright.Templates.Generator` |
| Collection expressions and slice patterns | Throughout |
| `GeneratedRegex` | Template placeholder scanning |
| `IsAotCompatible` + nullable everywhere | All three runtime packages |

## Deliberate limits

The short version of this section is a table: [conformance.md](conformance.md) says of every
format whether it is read, written, preserved or evaluated, and names where each one stops.
What follows is why.

- **The core has no layout engine.** Nothing in it paginates, measures a glyph or renders, which
  rules out resolving AutoFit column widths and answering a page count. Pagination exists in
  `Quillwright.Pdf` and is used only when rendering; it does not write anything back into the
  model, so a document that has been printed is the same document afterwards. What that renderer
  does and does not draw is in [pdf-export.md](pdf-export.md).
- **Markdown and HTML are semantic projections, not visual conversions — and they go both
  ways.** Body order, headings, links, lists, tables, notes and pictures survive where the
  target can carry them; pagination and floating geometry do not. The two exports share one
  paragraph walker, so they agree about fields, revisions and hidden text. Each importer maps
  the documented subset back into the model; it is not a promise that every export fallback is
  inverted. Every deliberate export approximation is returned in the respective diagnostics;
  the mappings and current import boundaries are in
  [markdown-export.md](markdown-export.md), [markdown-import.md](markdown-import.md),
  [html-export.md](html-export.md) and [html-import.md](html-import.md).
- **HTML is parsed, not pattern-matched.** The projection above is what the *importer* makes
  of a tree; the tree itself comes from an implementation of WHATWG HTML §13.2 — the whole
  tokenizer, the whole tree builder, the adoption agency algorithm and foster parenting
  included. It lives in the core package because it needs nothing but the primitives, and it
  is held to a browser's output rather than to its own idea of the standard.
- **Charts, embedded objects and web extensions are read but not authored.** `document.Charts`
  gives the kind, the title and the series — from the cache in a chart part, or from the record
  stream inside the Microsoft Graph object a legacy chart is stored as ([MS-OGRAPH]).
  The one write charts have is `document.SetChartData`: it replaces an existing chart's data —
  names, categories, values, and bubble sizes for each series, as literals inside the part,
  leaving how the chart looks alone. It is data replacement, not authoring: the series count is
  fixed, a new chart cannot be made, and the rewritten stretch no longer points into the
  embedded workbook, which keeps its old numbers for Word's own "Edit Data".
  `document.EmbeddedObjects` gives the program, the display name and the bytes of an embedded
  spreadsheet or an attached file, out of the streams of its compound file ([MS-OLEDS] 2.3).
  `document.WebExtensions` gives the add-ins a document will try to load and the state they
  saved ([MS-OWEXML]). Those parts are otherwise copied through untouched on save, so what is
  read is what a saved file carries. SmartArt has no API at all.
- **Two formats are read that a document never contains.** A co-authoring lock file
  ([MS-WORDLFF]) travels beside a document on the server that hosts it, and an Office add-in
  manifest ([MS-OWEMXML]) is distributed through an add-in catalogue. Neither can be found from
  a package, so neither hangs off `WordDocument`: `CoAuthoringLockFile` and
  `OfficeAddInManifestReader` take the bytes and hand back what they say. The lock reader covers
  the whole of `CT_CALocks` but authors nothing and speaks no protocol; the manifest reader
  covers the metadata the two base namespaces share and returns `VersionOverrides` as markup
  rather than pretending to model four more vocabularies.
- **Text boxes are containers; the shapes around them are not modelled.** The words inside a
  text box are reachable from `AllContainers`, so replace and text extraction find them, while
  the geometry, fill and wrapping are kept as the bytes they arrived as. Word writes the same
  words twice, once as a modern drawing and once as a VML fallback; the model holds them once
  and writes both, so an edit cannot leave the two disagreeing.
- **Macros are read but never authored.** `document.Macros` decodes the VBA project of a
  `.docm` or a `.doc` into module names, source, references and protection state; nothing writes
  one, and the `.doc` writer drops the project with a warning. A user form's controls are
  decoded too ([MS-OFORMS]) — what each one is, what it is called, where it sits and what it
  says — while their pictures, fonts and list contents are stepped over. See
  [macros.md](macros.md).
- **Fields are parsed and the deterministic ones evaluated; the rest are left dirty.** A
  formula, a date, a document property or a bookmark reference is recomputed by
  `document.UpdateFields()`; anything that needs a layout — `PAGE`, a `TOC` — keeps the result
  it arrived with and is marked for the consumer to build. See [fields.md](fields.md).
- **Revisions are recorded, read, accepted and rejected.** `document.TrackChanges(author)` opens
  a session in which every edit made through the ordinary API leaves a mark instead of
  rewriting the text; deleted text stays where it is under a `w:del` until the change is
  accepted. See [editing.md](editing.md).
- **Digital signatures are verified, and a saved package is signed.** `document.Signatures`
  says who signed, whether the signature value verifies against their key, and whether the
  parts it covers have changed since — three questions kept apart because a document can fail
  any one of them alone. `DocumentSigner.SignAsync` signs a saved file with the caller's
  certificate — signatures cover bytes, so signing is the last step, not a document method.
  Canonicalisation is written out in the library rather than taken from
  `System.Security.Cryptography.Xml`, which is not trim-safe; everything else both directions
  need is, so they live in the core package. Certificate trust is asked on demand under a
  policy the caller supplies. See [signatures.md](signatures.md).
- **Two documents compare into a redline.** `DocumentComparer.Compare` aligns blocks, diffs
  changed paragraphs word by word, and records the differences through the same machinery
  tracked edits use, so accepting them all yields the revised document and rejecting them all
  the original. `document.Append` copies one document into another with styles, numbering,
  media, notes and comments carried and remapped. See [editing.md](editing.md).
- **Encryption is read four ways and written one.** `SaveOptions.Password` locks a package with
  AES-256; `DocWriteOptions.Password` locks a `.doc` with the RC4 the format has. The older
  package scheme and the two legacy ones are read and never written.
- **`.doc` is read and written, but writing it is a conversion, not a round trip.** The binary
  format has no revisions, no content controls and no equations, so those are written as their
  accepted, unwrapped or flattened form and every substitution raises a warning. Reading is
  lossy in the other direction — a shape that is neither a text box nor a picture does not come
  across — and each loss raises a warning too. See [doc-import.md](doc-import.md) and
  [doc-export.md](doc-export.md).
- **Coverage of §17 is written up rather than claimed.** Which elements are modelled, which are
  preserved and where the library departs from the standard on purpose is in
  [wordprocessingml-coverage.md](wordprocessingml-coverage.md).
- **Theme colours keep their slot and can be resolved.** `document.ResolveColor` follows the
  slot through `w:clrSchemeMapping` into the theme's colour scheme and applies any tint or
  shade. The arithmetic is checked against Word's own cache: when Word writes a theme colour it
  stores the value it computed beside the name, so every such colour in the corpus is a worked
  example with the answer at the back. Most agree exactly and none is more than a single step
  out in one channel.
