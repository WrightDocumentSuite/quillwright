# RTF export

```bash
dotnet add package Quillwright.Rtf
```

```csharp
using Quillwright.Rtf;

RtfExportDiagnostics diagnostics = await RtfWriter.SaveAsync(document, "letter.rtf");
foreach (RtfExportWarning warning in diagnostics)
    Console.WriteLine(warning);
```

For an in-memory result, `RtfWriter.Save(document)` returns both `Content` and `Diagnostics`.
The byte stream is deterministic ASCII RTF: non-ASCII UTF-16 code units use signed `\uN?`
controls, and font and colour tables are sorted before their indexes are assigned. Stream
overloads leave the stream open.

## What is written

- Paragraph text, tabs, line/page/column breaks and section boundaries
- A font table and colour table for the resources used by emitted runs
- Common direct character formatting: font, bold, italic, capitals, strike, outline, shadow,
  emboss, imprint, visibility, proofing, size, colour, highlight, underline, baseline
  placement, spacing, scale, kerning, position, direction and language
- Common paragraph formatting: alignment, indents, spacing and line-spacing rule, keep flags,
  page-break-before, widow control, hyphenation, direction, contextual spacing, outline level
  and tab stops with leaders
- RTF 1.9.1 annotations: `atrfstart`/`atrfend` range bookmarks, `atnid` initials,
  `atnauthor`, `chatn`, `annotation`, `atnref`, minute-precision `atndate`, comment body
  paragraphs and Word-compatible flat replies through `atnparent -1`

Each run is isolated in its own RTF group and starts with `\plain`, so formatting cannot leak
between adjacent runs. The writer emits `\pard` for every paragraph for the same reason.
Saving the same model twice produces the same bytes independently of the current culture.

## Losses and diagnostics

RTF is used here as a semantic interchange format. It is not an alternative package for
preserving arbitrary WordprocessingML or an existing RTF file byte for byte.

- Tables and other unsupported block types are flattened to their visible text.
- Unsupported inline objects are omitted, or replaced by their text when they expose one.
- Field instructions and deleted runs are not written as visible text; inline wrappers and
  marks are unwrapped.
- A nested reply-to-reply chain is flattened to the RTF/Word flat reply list and reported as
  `ContentSkipped` with subject `comment-thread-depth`. A reply that is not adjacent to its
  thread root uses an explicit `atnparent` reference and reports `comment-thread-order`, because
  some Word versions flatten that form.
- OOXML resolved state and reactions have no RTF 1.9.1 field. They are omitted with precise
  `comment-resolved-state` and `comment-reactions` diagnostics; comment text, range, author,
  initials, date and the supported reply relationship are still written.
- RTF DTTM stores wall-clock fields only to the minute. Seconds and timezone identity from a
  `DateTimeOffset` cannot survive the conversion.
- Named styles, lists, section page geometry, borders, shading, theme-only values and the long
  tail of Word-specific formatting are not authored yet.
- Four distinct OOXML font slots collapse to one RTF font when they disagree. A theme colour
  without a resolved RGB value becomes automatic colour.

Every such approximation is added once to `RtfExportDiagnostics` as an
`UnsupportedBlock`, `UnsupportedInline`, `FormattingDropped` or `ContentSkipped` warning. This lets conversion
code reject a lossy result without guessing from the output:

```csharp
RtfExportResult result = RtfWriter.Save(document);
if (!result.Diagnostics.IsEmpty)
    throw new InvalidOperationException(result.Diagnostics.ToString());

await result.SaveAsync("letter.rtf");
```

Importing the supported subset back through `RtfReader` preserves its text and mapped direct
formatting. See [rtf-import.md](rtf-import.md) for parser limits and the import boundary.
