# RTF import

```bash
dotnet add package Quillwright.Rtf
```

```csharp
using Quillwright.Rtf;

RtfImportResult imported = await RtfReader.LoadAsync("letter.rtf");
WordDocument document = imported.Document;

foreach (RtfImportWarning warning in imported.Diagnostics)
    Console.WriteLine(warning);
```

The reader consumes RTF bytes directly. It does not start Word, use a RichEdit control, or
round-trip through HTML. The result is a semantic `WordDocument`: source control-word spelling,
unknown destinations and the original grouping are not retained for a later byte-identical
save.

## What is imported

- ANSI, Mac and PC document encodings, `\ansicpgN`, hexadecimal byte escapes and per-font
  `\fcharsetN`/`\cpgN` code pages, including multi-byte character sets
- `\uN` Unicode characters with their `\ucN` fallback, and `\upr`/`\ud` alternatives
- Paragraphs, sections, tabs, and line, page and column breaks
- Font and colour tables; the selected font, text colour, underline colour and fixed highlight
  palette
- Common character formatting: bold, italic, capitals, strike, outline, shadow, emboss,
  imprint, hidden and no-proof text, size, underline, superscript/subscript, spacing, scale,
  kerning, position, direction and language
- Common paragraph formatting: alignment, indents, spacing, line-spacing rule, keep flags,
  page-break-before, widow control, line-number suppression, hyphenation, direction, contextual
  spacing, outline level and tab stops with leaders
- The visible result of a field; its instruction is not inserted into ordinary document text
- RTF 1.9.1 comments and replies: `atrfstart`/`atrfend` ranges, point comments, `atnid`
  initials, `atnauthor`, structured `atntime`, packed-DTTM `atndate`, `atnref`, `atnparent`,
  multi-paragraph annotation bodies and the `chatn` reference marker

Formatting follows RTF group scope. A property changed inside `{...}` is restored at the
closing brace, while paragraph properties continue across `\par` until changed or reset by
`\pard`.

## Deliberate boundary

This reader is a conversion reader, not a lossless RTF editor. Style sheets, lists,
tables, headers and footers, notes, pictures, drawing objects and embedded objects
are not modelled from RTF yet. Resource destinations do not leak into body text. Recognised
content-bearing destinations that are skipped add an `RtfImportWarning`; unknown optional
destinations introduced by `\*` are skipped as the RTF specification requires.

Annotation destinations are recovered conservatively. A missing range start or end becomes a
point or partial-range comment, a backwards range falls back to the annotation position, an
unresolved `atnparent` remains top-level, and a bookmark with no annotation body is ignored.
Each case adds `RtfImportWarningKind.MalformedAnnotation` with a specific subject such as
`annotation-anchor`, `annotation-parent` or `annotation-orphan-anchor`; malformed review
metadata never leaks into ordinary body text.

The optional annotation picture in `atnicn` has no comment-icon model yet. It is skipped with
`UnsupportedDestination` subject `AnnotationIcon`; the range, author, date and comment body still
import normally.

Current Word writes `atnparent -1` for replies following a top-level annotation. The reader
attaches that reply to the nearest preceding root with the same range, so multiple replies form
one flat thread. It also accepts the spec-conforming explicit parent value matching an earlier
`atnid`; an earlier `atnref` is accepted only as a compatibility concession for non-conforming
writers. Only one preceding match is accepted. A future or duplicate id/reference stays
top-level and produces `MalformedAnnotation` with subject `annotation-parent`, so malformed input
cannot create a cycle or silently select the wrong parent.

For dates, a valid packed `atndate` is authoritative for year through minute, matching the value
Word exposes. `atntime` supplies the seconds that `atndate` cannot carry and is the fallback when `atndate` is absent;
that fallback needs a complete year/month/day and uses zero for omitted clock fields. Conflicting
or invalid fields produce `MalformedAnnotation` with subject `annotation-time`. RTF carries no
timezone, so the result is stored in `Comment.Date` with neutral offset zero while `DateUtc`
remains null; this does not claim the source time was UTC.

RTF has no equivalent of OOXML `intelligentPlaceholder`, so an empty annotation imports as an
ordinary empty comment. In particular, a follow-up exported by Quillwright cannot recover its
`IsFollowUp` flag on reimport; the export side reports this as `comment-follow-up` while keeping
the prompt text hidden.

Malformed structure raises `RtfFormatException`, including unmatched groups, content outside
the one root `\rtf1` group, invalid numeric controls and truncated `\binN` data. Import also
has explicit resource limits:

```csharp
using Quillwright.Diagnostics;

var options = new RtfImportOptions
{
    Budget = DocumentLoadBudget.Default with
    {
        MaxInputBytes = 32L * 1024 * 1024,
        MaxMarkupDepth = 128,
        MaxMarkupNodes = 250_000,
        MaxTextCharacters = 4_000_000,
    },
};

RtfImportResult imported = await RtfReader.LoadAsync("untrusted.rtf", options);
```

`RtfImportOptions.Budget` uses the same `DocumentLoadBudget` as the other importers.
`MaxInputBytes`, `MaxGroupDepth` and `MaxTextCharacters` remain backward-compatible aliases for
`Budget.MaxInputBytes`, `Budget.MaxMarkupDepth` and `Budget.MaxTextCharacters`. The defaults are
128 MiB, 256 nested groups, two million materialized markup nodes and 16 million UTF-16 code
units. For RTF, `MaxMarkupNodes` counts each materialized body or annotation paragraph, added
section, completed annotation record, imported comment and recognised annotation range endpoint;
raw control words and skipped optional destinations are not counted as model nodes. This prevents
text-free `\par` or empty-annotation floods from bypassing the decoded-text limit.

Budget breaches retain the reader's legacy `RtfFormatException` contract rather than exposing
`DocumentLoadLimitException` and its machine-readable limit fields. `\binN` payloads are consumed
by their declared length, so brace and backslash bytes inside a picture cannot change parser
structure. Stream overloads leave the caller's stream open. Their cancellation token remains
active after the source has been read, throughout tokenization, annotation resolution and model
materialization. See
[Loading untrusted input](loading-untrusted-input.md) for the shared limits and deployment
guidance.

The implementation follows the local RTF 1.9.1 grammar for groups, destinations, Unicode,
font/colour tables and paragraph and character properties. Current format-level coverage is
summarised in [conformance.md](conformance.md); the opposite conversion is in
[rtf-export.md](rtf-export.md).
