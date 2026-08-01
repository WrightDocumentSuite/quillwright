# Legacy `.doc` export

```bash
dotnet add package Quillwright.Doc
```

```csharp
using Quillwright.Doc;

WordDocument document = await WordDocument.LoadAsync("report.docx");
await DocWriter.SaveAsync(document, "report.doc");
```

Writing is direct: the compound file, the header, the piece table and the formatting pages are
all produced in managed code, with no dependency on Office, on Windows, or on a conversion
service. `DocWriter.Save` returns the bytes if you would rather not touch the disk.

## What writing this format actually involves

A `.docx` is a zip of XML: the structure of the document is the structure of the file. A `.doc`
is the opposite. There is one flat run of characters, and everything else — where the
paragraphs are, what they look like, which of them are table cells, where the footnotes belong
— is recorded elsewhere as positions into that run. Writing one is therefore a linearisation
followed by a set of cross-referenced tables, and every table has to agree with every other.

**The container.** The file is a small file system ([MS-CFB]) with three streams:
`WordDocument` for the header, the text and the formatting pages, `1Table` for everything
located by position, and `Data` for what fits in neither. Streams under 4 KB do not get sectors
of their own — they are packed into a second, finer allocation inside a stream owned by the
root entry, and a short document's table stream always lands there.

**The text.** All stories share one run of characters, in the order the header counts them:
main text, footnotes, headers and footers, comments, endnotes. Structure is expressed by
reserved characters inside it. A paragraph ends with 0x0D; a table cell ends with 0x07; a
section ends with 0x0C in place of the paragraph mark; a field is 0x13, its instruction, 0x14,
its result and 0x15; a picture is a single 0x01 whose character properties name an offset in
the data stream.

**The formatting.** Character and paragraph properties are packed into 512-byte pages that fill
from both ends: file offsets from the front, property lists from the back, and a count in the
very last byte. A page holds at most 0x65 character runs or 0x1D paragraphs, and a single
paragraph's properties can be too large for a page at all — a table sixty-three columns wide
needs about 1.4 KB for its row definition — in which case the properties move to the data
stream and the page holds one modifier pointing at them.

**The version.** The header is written in the shape Word emits rather than the older shape the
specification also allows: version 0x00C1 with an extension that supersedes it with 0x0112, a
directory of 183 offset pairs, and a 694-byte document-properties block. Word refuses to open
a file written in the plain Word 97 shape, which is the sort of thing only an oracle test
finds.

## What is written

- Text in every story, always as UTF-16, with tabs, line, page and column breaks
- Character formatting: bold, italic, strike, double strike, outline, shadow, emboss, imprint,
  small caps, caps, hidden, web-hidden, no-proof, underline, size, colour, highlight,
  superscript and subscript, position, kerning, character spacing, scale, and the ascii,
  East Asian and complex-script fonts
- Paragraph formatting: alignment, indents including hanging, spacing, line spacing and its
  rule, keep-with-next, keep-lines-together, page-break-before, widow control, contextual
  spacing, outline level, tab stops, borders and shading
- Styles, with Normal and the nine heading levels in the fixed slots Word recognises
- Sections: page size, orientation, margins including header, footer and gutter, columns,
  break kind, title page, page numbering scheme and starting number
- Tables, including nested tables, column spans, vertical merges, header rows, cell widths
  and cell shading
- Headers and footers, for the default, first and even-page slots of every section, and the
  document-wide setting that decides whether the even-page ones are used at all
- The document's own properties: title, subject, author, keywords and dates
- Footnotes and endnotes, including the ones whose mark the author chose rather than a number
- Comments, with their authors, initials and the stretch of text each one is about — the
  extent is a bookmark of its own, which the comment's record points at — and, in the parallel
  `AtrdExtra` array, the date each was written and which comment it answers
- Fields, with the field type derived from the instruction; hyperlinks are written as
  `HYPERLINK` fields
- Bookmarks, including overlapping ones and ones that span paragraphs
- Pictures, as PNG or JPEG in the data stream
- Numbering: list definitions, their nine levels and the overrides paragraphs point at

## What is not

The binary format is older and narrower than the model, so some content cannot be written as
what it is. Every substitution raises a warning through `DocWriteOptions.OnWarning` rather than
disappearing:

| Content | What happens |
| --- | --- |
| Tracked revisions | Written as the text with the revision accepted |
| Content controls | Written as their contents, without the control |
| Equations | Flattened to the text inside them, losing the layout |
| Other markup preserved verbatim from a `.docx` | Dropped, with a warning |
| Images other than PNG and JPEG — a metafile, a PICT, a TIFF | Dropped, with a warning: the writer authors the two OfficeArt records it can build correctly, and the reader's wider coverage is not matched here |
| Floating pictures | Written inline |
| A comment marked resolved, its reactions, and the author identities of `people.xml` | Dropped, with a warning; the comment, its date and what it answers survive |
| A VBA project | Dropped, with a warning; see [macros.md](macros.md) |

An equation is worth singling out. The binary format has no equation of its own, and dropping
one would leave a hole where the reader saw a formula, so the text inside it is written as
ordinary text: a fraction such as `x=1/2` becomes `x=12` when the bar is lost. That is lossy and
says so through a warning, but it keeps
the content of the document rather than only its shape. An equation a caller built rather than
read has no markup to take the text from, so it is written as the tree reads out — `x=1/2` —
which says more, not less. See [math.md](math.md).

```csharp
var options = new DocWriteOptions
{
    OnWarning = warning => Console.WriteLine(warning.Message),
    WriteImages = true,
    WriteHyperlinks = true,
};

byte[] bytes = DocWriter.Save(document, options);
```

## Locking the file

`DocWriteOptions.Password` encrypts the document, table and data streams with RC4 CryptoAPI
([MS-OFFCRYPTO] 2.3.5), which is what the binary format has. It is weak by any modern measure,
and it is offered because refusing would leave a caller converting an archive of locked files
with nothing to write them back as. A document that should actually be protected belongs in a
`.docx` saved with `SaveOptions.Password`, where the key is AES-256.

The encryption header goes at the front of the table stream, and every offset the file
information block records is measured from there — so the space for it is reserved before
anything else is written rather than prepended afterwards.

## Verification

Three kinds of test, because each catches what the others cannot.

**Symmetry.** Every property the writer encodes is parsed straight back by the reader's
translator and compared. Character opcodes in this format sit one apart from neighbours that
mean something entirely different — `sprmCFOutline` is 0x0838 and `sprmCFCaps` is 0x083B, with
shadow, small caps and hidden text in between — and nothing but a round trip catches a
mistake there.

**Corpus.** All 249 legacy documents and all 934 modern documents in the reference corpora are
read, written as `.doc`, and read again; the visible text has to match exactly.

**Word.** The other two tests check the writer against this library's own reader, which shares
its understanding of the format and therefore shares any misunderstanding. Word is the only
judge of whether a file is really valid. The oracle tests write a document, open it through
Word automation read-only, and require that it opens without a repair prompt and reads back
its own text. They need Word installed and are opt-in:

```powershell
$env:QUILLWRIGHT_WORD_ORACLE = "1"
dotnet test --filter "Category=word-oracle"
```

They earn their keep, and not only for whether a file opens. The oracle asks Word what it
sees: which words a comment highlights, whether the even pages have a header of their own,
what the document's title is. That is how it caught the two-byte header the font table does
not have, the paragraph mark each sub-document needs past its last story, comment authors
being a bare array of counted strings rather than a string table, the flag named
`fFacingPages` that quietly means "even and odd pages differ", and the rule that a comment's
bookmark has to end exactly where its reference character sits.
