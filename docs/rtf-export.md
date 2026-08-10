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
  `atnauthor`, second-precision `atntime`, Word-compatible packed `atndate`, `chatn`,
  `annotation`, `atnref`, comment body paragraphs and flat replies through `atnparent -1`

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
- In a contiguous thread, Word's `atnparent -1` convention flattens a nested reply-to-reply
  chain to the thread root and reports `comment-thread-depth`. A non-adjacent reply instead
  uses its immediate parent's RTF annotation id in `atnparent` (not the numeric range `atnref`),
  which preserves the exact parent for a conforming reader; it reports `comment-thread-order`
  because Word versions may flatten that form.
- An explicit parent needs a non-empty annotation id that no other possible parent shares. If
  the parent's initials are absent or ambiguous, the writer does not invent an id or risk
  attaching the reply to the wrong message: it omits `atnparent`, exports the reply at top level
  and reports `comment-parent-annotation-id` as well as `comment-thread-order`.
- OOXML `intelligentPlaceholder` has no RTF 1.9.1 field. Its prompt is service text rather
  than a user-authored remark, so a top-level follow-up retains an empty anchored annotation
  instead of exposing that prompt as comment text. The lost role is reported as
  `comment-follow-up`.
- OOXML resolved state and reactions have no RTF 1.9.1 field. They are omitted with precise
  `comment-resolved-state` and `comment-reactions` diagnostics; comment text, range, author,
  initials, date and the supported reply relationship are still written.
- `atntime` retains wall-clock fields to the second, while the parallel packed `atndate` keeps
  Word compatibility at minute precision. Neither form has a timezone or subsecond field, so
  offset identity and fractional seconds from a `DateTimeOffset` cannot survive the conversion.
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
