# WordprocessingML coverage

A sweep of ISO/IEC 29500-1 §17 against the reader, the writer and the preservation slots. The
question it answers is not "what does the model understand" — that list is in
[model.md](model.md) — but the narrower and more important one: **is there anything a document
can say that this library would lose?**

This page is about WordprocessingML and nothing else. The binary `.doc` format, the drawing
layer, the chart records and the two standalone formats that never appear in a package are
different specifications with different boundaries, and forcing them into a table of §17
elements would say nothing true about any of them; they are in
[conformance.md](conformance.md).

## The count

§17 declares 654 named things: 107 simple types and 547 elements. Of the elements, 389 are
named literally in the reader. The other 158 are all accounted for below, and none of them is
dropped.

## Where the other 158 go

**Parts copied through whole.** `fontTable.xml`, `webSettings.xml`, the theme and the glossary
document have no reader at all: they are carried from the loaded package to the saved one byte
for byte, with their relationships and content type. Everything they declare is therefore safe
by construction — the font descriptors (`panose1`, `sig`, `charset`, `family`, `pitch`, the
`embed*` family), the web layout (`divs`, `blockQuote`, `bodyDiv`, the `mar*` margins,
`frameset` and its frames), and the whole glossary (`docParts`, `docPart`, `docPartPr`,
`docPartBody`, and the categories and behaviours under them).

The theme's twelve colours are the one exception: they are read out into `document.Theme` as
well as copied, because a run that names a theme slot has a name and no value without them.
`document.ResolveColor` turns a `WordColor` into what it actually shows as, following the slot
through the mapping in `w:clrSchemeMapping` and applying `w:themeTint` or `w:themeShade`. The
rest of the theme — its fonts, fills, line styles and effects — stays bytes.

**`settings.xml`.** `CT_Settings` has around ninety optional children in a fixed order, so the
part is held as its elements in document order rather than modelled one by one; a handful have
typed accessors and the rest survive untouched at their schema position. That covers the
compatibility switches (`adjustLineHeightInTable`, `applyBreakingRules`,
`balanceSingleByteDoubleByteWidth`, `doNotExpandShiftReturn`, `spaceForUL`, `compatSetting`),
the revision-save identifiers (`rsidRoot`), the document variables (`docVar`), and the entire
mail-merge tree (`odso`, `dataSource`, `recipients`, `fieldMapData` and the two dozen elements
beneath them).

**Named slots inside modelled parts.** Where the model does interpret a part, anything it does
not interpret goes into a slot of its own rather than a shared bag, because `CT_RPr` and
`CT_PPr` declare their children in a fixed order and appending at the end produces a file Word
repairs. So:

- form fields (`ffData`, `checkBox`, `ddList`, `textInput`, `calcOnExit`, `helpText`,
  `statusText`, `entryMacro`, `exitMacro`) ride on the field character that opens them;
- content-control properties (`equation`, `comboBox`, `dropDownList`, `richText`, `date` and
  its `calendar`/`dayLong`/`monthShort` formatting, `docPartList`, `docPartObj`, `dataBinding`,
  `placeholder`, `showingPlcHdr`, `temporary`, `lock`, `citation`, `bibliography`) ride on the
  control;
- latent styles (`lsdException`) ride on the style sheet;
- note properties inside a section (`numRestart`, `numStart`) ride on the section;
- a row's overrides of its table's formatting (`tblPrEx`, `tblPrExChange`) ride on the row.

**Verbatim fragments in the body.** Ruby text (`ruby`, `rubyPr`, `rubyBase`, `rt`, `hps`),
`altChunk`, `contentPart`, `movie`, `pgNum`, `annotationRef`, smart tags and custom XML
(`smartTagPr`, `customXmlPr`, `attr`) are kept as the bytes they arrived as and written back in
place. `object` is kept the same way and additionally read into
[`WordDocument.EmbeddedObjects`](../src/Quillwright/Model/EmbeddedObject.cs), so an attachment
can be pulled out without the markup changing.

## What the sweep found and fixed

Four places where a modelled part discarded markup rather than carrying it. Each has a test in
`WordprocessingMlCoverageTests`.

- **`w:tblPrEx` (§17.4.61)** was skipped, so a row pasted from a table with different borders
  lost them. It now has a slot of its own and is written first in the row, where the schema
  wants it.
- **Run-level marks between rows (§17.4.78)** — a bookmark or permission range that opens
  between two `w:tr` elements — were skipped, which left the closing mark dangling and the
  reference broken. They are kept and written after the last row, which the content model
  allows.
- **Run-level marks between cells (§17.4.78)** were skipped for the same reason and are kept
  the same way.
- **`w:numIdMacAtCleanup` (§17.9.14)**, the identifier Word remembers for its own renumbering,
  was skipped. It now has a slot and is written last in the part.

## Where the library departs from the standard on purpose

Checked against [MS-OI29500], which records what Word actually does. Each has a test in
`ConformanceTests`.

- **`ST_OnOff` (§22.9.2.7).** ISO narrowed the type to `xsd:boolean`; ECMA-376 also allowed
  `on` and `off`, and Word still writes them. Both are read, along with the whitespace the
  boolean datatype collapses. Only `1` and `0` are written.
- **Universal measures (§22.9.2.15).** `ST_TwipsMeasure`, `ST_SignedTwipsMeasure`,
  `ST_HpsMeasure` and `ST_MeasurementOrPercent` are unions: the number may carry one of six
  unit identifiers instead of being in the attribute's own unit. Around 240 attributes of §17
  take one of those types, and a Strict producer uses the spelling freely — `w:ind w:start="36pt"`,
  `w:tcMar` in `pt`. Both spellings are read and converted; twips are written, which the
  standard permits since units of measure need not be preserved. Reading only the bare number
  did not fail loudly; it read as an absent attribute. Every indent of a Strict document, its
  styles and its numbering silently disappeared.
- **Toggle properties (§17.7.3).** A `basedOn` chain is one layer of the hierarchy, so the most
  derived style that states a toggle wins rather than the chain exclusive-oring — the standard
  says so and the library used to get it wrong. Across layers the exclusive-or applies as
  specified, and to exactly the twelve properties §17.7.3 lists: `b`, `bCs`, `caps`, `emboss`,
  `i`, `iCs`, `imprint`, `outline`, `shadow`, `smallCaps`, `strike`, `vanish`. `dstrike`, `rtl`
  and `cs` are not among them and overwrite like any ordinary property. The one deliberate
  departure: a toggle the document defaults turn on is said by §17.7.3 to settle the matter,
  and here it takes part in the exclusive-or like any other layer, because that is what Word
  does ([MS-OI29500] 2.1.230(a)).
- **Content types (ECMA-376 part 2 §7.2.3.5).** An override beats the extension defaults, both
  are matched without regard to case, and the extension is what follows the last dot *of the
  last segment* — taking it from the whole path gave `/word/media.v2/logo` an extension of
  `v2/logo`.
- **Strict relationship names.** The Strict vocabulary spells two roles differently rather than
  just under a different base: `extendedProperties` and `customProperties` against the
  Transitional `extended-properties` and `custom-properties`. Swapping the base alone left a
  Strict package with two relationships to the same part, which Word rejects.
- **Strict element and attribute names.** Strict also renamed the direction words that assume a
  left-to-right page, and the rename is not uniform. `CT_Ind` takes `start`/`startChars` and
  `end`/`endChars`; `CT_TblBorders`, `CT_TcBorders`, `CT_TcMar` and `CT_TblCellMar` take
  `w:start` and `w:end`; `ST_Jc`, `ST_JcTable` and `ST_TabJc` dropped `left` and `right` from
  their enumerations outright. `CT_PBdr` and `CT_PageMar` kept `left` and `right`, so renaming
  every occurrence of the word would be the same mistake pointing the other way. Both spellings
  are read; which one is written follows the package. The one place this is not applied is
  `w:lvlJc`, below.
- **`w:lvlJc` in a Strict package (§17.9.7).** The standard types it as `CT_Jc` and
  [MS-OI29500] 2.1.281(a) says Word supports `start`, `center` and `end` there — and Word does
  write `start`, 29 times against 30 of the Transitional spelling across the reference corpus.
  The Open XML SDK, which this library validates against, rejects the standard spelling; it
  rejects it in files Word wrote and nothing has touched. The Strict spelling is written and
  that single validation error is suppressed in the test oracle, with the reasoning recorded
  beside it.
- **Character-unit paragraph indents (§17.3.1.12).** `startChars`/`endChars`, their Transitional
  `leftChars`/`rightChars` spellings, and `firstLineChars`/`hangingChars` are modelled in
  hundredths of a character and written back with their twip counterparts. Style resolution
  also applies Word's zero-clears-inherited rule from [MS-OI29500] 2.1.44; these attributes used
  to disappear even though 27 documents in the reference corpus carry them.

## Deliberately not covered

Fields are round-tripped as instruction and cached result, and the ones whose value follows
from the document are recomputed on request; the rest are marked dirty rather than guessed at,
because their value depends on a layout this library does not compute. Which is which is in
[fields.md](fields.md). Revisions are recorded, read, accepted and rejected; see
[editing.md](editing.md). There is no layout engine, so nothing in §17 that describes pagination
is acted on — it is stored and handed back. See [architecture.md](architecture.md) for the full
list.
