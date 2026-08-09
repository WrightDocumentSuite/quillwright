# Legacy `.doc` import

```bash
dotnet add package Quillwright.Doc
```

```csharp
using Quillwright.Doc;

WordDocument document = await DocReader.LoadAsync("archive.doc");
await document.SaveAsync("archive.docx");
```

Reading is direct: the compound file, the piece table and the formatting streams are parsed in
managed code, with no dependency on Office, on Windows, or on a conversion service. What comes
across and what does not is below; how it compares with the other formats is in
[conformance.md](conformance.md).

The default resource budget applies to the complete CFB file, its directory and streams, decoded
images and reconstructed object-pool payloads. Override it without an unbounded `ReadAllBytes`
through `DocImportOptions`:

```csharp
var options = new DocImportOptions
{
    Password = password,
    Budget = DocumentLoadBudget.Default with { MaxInputBytes = 64 * 1024 * 1024 },
};
WordDocument document = await DocReader.LoadWithOptionsAsync("archive.doc", options);
```

For a document already held in memory, use `DocReader.LoadWithOptions(bytes, options)`. The
distinct options method names preserve source compatibility with existing calls such as
`DocReader.Load(bytes, null)`, where `null` is the optional password.

See [loading-untrusted-input.md](loading-untrusted-input.md) for every limit and the common
`DocumentLoadLimitException` contract. The existing `LoadAsync(path, password)` and
`Load(bytes, password)` overloads keep their API and use the default budget.

## How the format works, and why reading it is a join

A Word 97-2003 file keeps the text in one long stream and everything about it somewhere else.

**The container.** The file is a small file system ([MS-CFB]) holding named streams in a
directory tree, with each stream's bytes scattered across sectors and chained through an
allocation table. Streams under 4 KB live in a second, finer allocation inside a stream of
their own. `WordDocument` holds the text and the formatting pages; `1Table` or `0Table` holds
everything else.

**The header.** The file information block at the start of `WordDocument` says which table
stream is live, how the text divides into stories, and where every other structure lives. Its
offsets are not fixed: the block grew over the versions, and the arrays that precede the
directory of file offsets each declare their own length, so it has to be walked.

**The piece table.** Word never rewrote a document from the start when you typed in the middle
of it. It appended the new text and recorded a piece pointing at it, so the character order a
reader sees is the order of the pieces, not the order of the bytes. Each piece is
independently single-byte or UTF-16, which is why decoding follows the table rather than the
stream.

**The formatting pages.** Properties live in 512-byte pages indexed by byte offset into the
text. A page lists the offsets it covers, then packs the property lists from the other end,
which is what makes the layout look inside out. Paragraph boundaries come from these pages too:
the text stream has no structure of its own.

**The property lists.** Each property is a two-byte opcode plus an operand whose length is
encoded in the top three bits of the opcode, so a reader can step over what it does not know
without losing its place. That is what makes it safe to understand only what matters.

## What comes across

- Text, in the right order, with both single-byte and Unicode pieces decoded
- Character formatting: bold, italic, strike, double strike, outline, shadow, emboss, imprint,
  small caps, caps, hidden, web-hidden, no-proof, underline, size, colour (both the
  sixteen-colour palette and true colour), highlight, superscript and subscript, position,
  kerning, character spacing, scale, and the ascii, East Asian and complex-script fonts
- Paragraph formatting: alignment, indents including hanging, spacing, line spacing and its
  rule, keep-with-next, keep-lines-together, page-break-before, widow control, contextual
  spacing, outline level, numbering references, tab stops, borders and shading
- Style names, mapped onto style identifiers in the converted document
- Sections, with page size, orientation, margins, columns, break kind and page numbering
- Tables, rebuilt from the row-end and in-table markers the format stores them as, including
  nested tables, column spans, vertical merges, header rows, column widths and cell shading
- The document's own properties and settings: title, author, dates, the default tab stop and
  whether even and odd pages have different headers
- Headers and footers, split back out of the single story that holds all of them
- Footnotes and endnotes, with their references placed back in the text
- Comments, with their authors, initials, references and the stretch of text each is about,
  plus the date and the reply threading the `AtrdExtra` array carries
- Fields, as their three boundary characters; `HYPERLINK` fields are collapsed back into
  hyperlinks, including ones nested inside a table of contents
- Bookmarks, including overlapping ones and ones that span paragraphs
- Pictures, taken back out of the data stream as their original bytes, in all seven of the
  formats the drawing layer stores ([MS-ODRAW] 2.2.23): PNG, JPEG, TIFF, bitmap, EMF, WMF and
  Macintosh PICT. The three metafile kinds are expanded from the deflate they are stored in, and
  a device-independent bitmap gets the file header a `.bmp` needs put back on. A PICT is carried
  across as `image/pict` media — nothing here draws QuickDraw, and a renderer downstream may not
  either, but the bytes reach the package intact rather than being dropped
- Floating pictures, resolved through the drawing layer ([MS-ODRAW]): the anchor in the text
  names a shape, the shape's `pib` property names a place in the store the whole document
  shares, and that is where the image bytes are. They arrive as a `Picture` with
  `IsInline = false`, sized from the anchor's rectangle and placed from its flag word
- Where a floating picture sits and what the text does about it, in `Picture.Anchor`: the
  origin the position is measured from, the offset or the edge it lines up with, the kind of
  wrapping and which sides text may flow down, and whether the picture is behind the text. It
  is read from two places, because the format says it in two — the flag word at the end of the
  anchor ([MS-DOC] 2.9.253) and the `posh`/`posrelh`/`posv`/`posrelv` properties of the drawing
  ([MS-ODRAW] 2.3.4.19-2.3.4.22), the latter being what distinguishes a centred watermark from
  one at the top-left corner
- Numbering definitions, their levels and the label patterns
- Page, line and column breaks
- Macros, decoded out of the `Macros` storage into module names and source; see
  [macros.md](macros.md)
- Text boxes, anchored where the text says they belong, both those in the body and those in a
  header ([MS-DOC] 2.3.6 and 2.3.7)
- Embedded objects, out of the `ObjectPool` storage: the program that owns them, their bytes,
  and the plain file they wrap when they wrap one
- Charts, decoded out of the embedded Microsoft Graph object they are stored as ([MS-OGRAPH]):
  the series, their names, their categories and their values, read from the grid of cells in
  the object's own `Workbook` stream. The styling stays inside the object, which is preserved
- Custom document properties, out of the user-defined half of the document summary
- The password of an encrypted document, given one: pass it to `DocReader.Load`

## What does not, and how you find out

A shape that is neither a text box nor a picture does not come across, nor do revision marks,
nor the styling of a chart. None of that happens quietly: each raises a
[`DocumentWarning`](../src/Quillwright/Diagnostics/DocumentWarning.cs) in
`document.LoadDiagnostics`, once per document however many times it occurs, so a caller
converting an archive can tell which files came through whole.

| What was found | What the reader does |
| --- | --- |
| A text box (`ccpTxbx`, joined to its shape by `FTXBXS.lid`) | Becomes a `TextBox` container where its anchor says, keeping the fill and outline the drawing states; the rest of the shape is not converted, and one warning says so |
| A shape showing a picture ([MS-ODRAW] `pib`, 2.3.23.5) | Becomes a floating `Picture` sized and placed from the anchor; the shape's own decoration is not modelled |
| A WordArt shape (`gtextUNICODE`, 2.3.22.1) | Its words become a text box, because they are stored in the shape and nowhere else; one warning says the lettering was not kept |
| Any other floating shape (`U+0008`, [MS-DOC] `plcfSpa`) | Nothing is put in its place; one warning names which shape it was |
| A picture whose stored form it cannot decode | Left out; one warning, coded `UnresolvedMedia` |
| An embedded OLE object (`sprmCFOle2` on a field separator) | Read into `document.EmbeddedObjects` out of the object pool |
| A Microsoft Graph chart | Read into `document.Charts` with its series, categories, values and bubble sizes, out of the `Workbook` stream of the object ([MS-OGRAPH]); a combined chart says what each series is drawn as, a trendline or an error bar is left out of the series, and one whose data cannot be decoded is listed by name with a warning |
| A text box nothing in the text points at | Flattened to paragraphs at the end of the document, with a warning, rather than lost |
| Customised toolbars or key bindings (`fcCmds`, a `Tcg`; the identifiers are tabulated in [MS-CTDOC]) | Left behind with a warning: those belong to Word itself and no later format has anywhere to put them |

Files older than Word 97 are refused with a `DocFormatException` that says why. A locked one
opens with `DocReader.Load(bytes, password)` — both RC4 schemes of [MS-OFFCRYPTO] 2.3.5 and
2.3.6, and the XOR obfuscation of 2.3.7 that came before them — and without a password, or with
the wrong one, raises an `EncryptedDocumentException`, the same type the `.docx` reader raises,
so one `catch` covers both formats.

## Verification

Every one of the 249 legacy documents in the Telerik reference corpus is read and saved as
`.docx`, and every saved package validates against the ISO/IEC 29500 schema through the Open XML
SDK. Separate tests assert that text, bold runs, font sizes, paragraph styles and tables
actually survive rather than merely not crashing — because a reader that returns an empty
document for everything would pass a crash test.

The warnings are held to the same bar: a test requires every kind of loss in the table above to
turn up somewhere in that corpus, so none of them is code that never runs. One of those
warnings no longer appears, by design: since WordArt started coming across, no shape in the corpus
is left behind at all, and a test asserts that too.

## What the corpus says the drawing layer holds

[MS-ODRAW] describes several hundred records and over a thousand shape properties, and says
nothing about which of them Word writes. The reader is built to a sweep of the corpus instead,
kept as tests in `OfficeArtInventoryTests` so that a corpus that changes says so:

| | Count |
| --- | ---: |
| Documents with a drawing region | 247 |
| Shapes | 554 |
| — the group each story is wrapped in | 264 |
| — rectangles, which is what a text box is drawn as | 247 |
| — picture frames | 24 |
| — lettering | 16 |
| — shapes drawn as a text box outright | 3 |
| Shapes with geometry of their own (`pVertices`, `pSegmentInfo`) | 0 |
| Shapes with a shadow | 0 |
| Groups inside a group | 0 |

That is why the reader models a fill, a line, a rotation and lettering, and does not model
coordinate transforms or custom paths: there is no document in the corpus that would exercise
either, and building for the specification's table of contents rather than for what producers
write is how a converter ends up with code nobody can test.

The reader is also exercised from the other direction: everything the writer described in
[doc-export.md](doc-export.md) is written and read back, which is what caught the style names
being read at a fixed offset the header actually declares, and the table definition being read
one byte long.
