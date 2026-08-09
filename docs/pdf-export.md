# Rendering to PDF

`Quillwright.Pdf` turns a `WordDocument` into a PDF: it paginates the text with real font
metrics and draws the result through [Inkwright](../../../PDFLib/Inkwright/README.md). No
external tool is shelled out to, so it runs the same on a build agent as on a desktop.

```csharp
using Quillwright.Model;
using Quillwright.Pdf;

WordDocument document = await WordDocument.LoadAsync("contract.docx");
PdfExportDiagnostics diagnostics = document.SaveAsPdf("contract.pdf");

foreach (PdfExportWarning warning in diagnostics)
    Console.WriteLine(warning);
```

The render never throws for content it cannot draw. It draws what it can and says in the
diagnostics what it left out, so a batch job can go on and a person can see afterwards which
files need looking at.

## Composing rather than saving

`SaveAsPdf` is the short way round. When the result has to be signed, encrypted or made
archival, render it and keep the document:

```csharp
using Inkwright;
using Inkwright.Layout;

PdfExportResult result = PdfExporter.Render(document, new PdfExportOptions { Tagged = true });
using PdfDocument pdf = result.Document;

PdfUaProfile.Declare(pdf, PdfUaConformance.Ua1, "Quarterly report");
pdf.Save("report.pdf");
```

## Fonts

A document names families; a machine has files. The lookup is a chain, and every step past the
family the document asked for is recorded as a `FontSubstituted` warning:

1. the files the caller registered in `PdfExportOptions.FontFiles`,
2. the fonts installed on the machine,
3. a table of faces that are metrically or visually close — Carlito for Calibri, Liberation
   Serif for Times New Roman, and so on,
4. `PdfExportOptions.FallbackFontFamily`,
5. one of the fourteen fonts every reader carries, chosen by what the family name suggests.

On a server with no fonts installed, register the faces you ship:

```csharp
var options = new PdfExportOptions();
options.FontFiles["Calibri"] = "/srv/fonts/Carlito-Regular.ttf";
options.FontFiles["Calibri Bold"] = "/srv/fonts/Carlito-Bold.ttf";
```

Faces are embedded once per document and, unless `SubsetFonts` is turned off, carry only the
glyphs the document actually used.

Installed TrueType/OpenType faces are matched by the family and PostScript metadata inside the
font file, not by the filename alone. Legacy Word/PDF names (`Times New Roman CYR`, `Times-Roman`,
subset-prefixed PostScript names) therefore select the installed Times New Roman face and keep the
requested bold/italic variant instead of falling through to a substitute. A Windows compatibility
fallback also recognises the abbreviated Office filenames (`times*.ttf`, `cour*.ttf`, `arial*.ttf`)
when Quillwright is consumed with an older pinned Inkwright package.

## What is laid out

**Text.** Line breaking at spaces, hyphens and slashes, with a word too long for any line split
by character. Left, centre, right and justified alignment; left, right, first-line and hanging
indents; the four line-spacing rules; space before and after, including the contextual spacing
that keeps list items from drifting apart.

**Hyphenation.** A soft hyphen (`w:softHyphen`) is invisible except at the break it marks,
where it draws as a hyphen — including the width reservation, so the hyphen that may be needed
is never the thing that does not fit. When the document asks for automatic hyphenation
(`w:autoHyphenation`), words break at the points a Liang pattern set allows, chosen per run by
its `w:lang` — the whole tag first, then the primary subtag, so one set under `en` serves
`en-US` and `en-GB` alike. The library ships no patterns, because the standard files — TeX's
`hyphen.tex`, the `hyph-*.dic` dictionaries LibreOffice uses — carry licences of their own;
load one into `HyphenationPatterns.Parse` (both the TeX form and the one-per-line dictionary
form are read, exceptions included) and register it:

```csharp
var options = new PdfExportOptions();
options.HyphenationPatterns["en"] = HyphenationPatterns.Load("hyph-en-us.dic");
document.SaveAsPdf("report.pdf", options);
```

`w:suppressAutoHyphens` exempts a paragraph and `w:doNotHyphenateCaps` exempts words in
capitals, both honoured. A language no set covers wraps whole and is named once in the
diagnostics. The hyphenation zone and `w:consecutiveHyphenLimit` are not honoured: the zone
trades raggedness against hyphens in ways that differ by justification, and neither changes
what a break means, only how often one is taken.

**Characters.** Bold and italic through the font, plus colour, underline in every style the
format defines, single and double strikethrough, highlighting, character shading, superscript
and subscript, capitals and small capitals, character spacing and horizontal scaling. Hidden
text is left out unless `IncludeHiddenText` says otherwise, and text deleted under revision
tracking is never printed.

**Tabs.** Left, centre, right, decimal and bar stops, the implicit stop a hanging indent
creates, the default grid from `w:defaultTabStop`, and dot, hyphen and rule leaders.

**Lists.** Counters per list instance and level, with start overrides, restart rules and the
`%1` templates that print the levels above. Bullets are drawn in the font the level names — and
only the bullet is, which is why the text beside it stays in the body font.

**Tables.** Column widths from the grid, from the table's preferred width, from cell
preferences, or measured from the content when the table brought no grid at all. Horizontal and
vertical merges, cell padding, shading, vertical alignment and the border conflicts between
neighbours. Header rows repeat at the top of every page; a row that will not fit moves whole or
is broken, depending on whether it allows it. Tables nest. Named table styles and their conditional
first/last-row, first/last-column and banded regions are resolved into the table, row and cell
formats used by layout, not only into the text inside the cells.

**Sections.** Page size, orientation, margins and gutter per section; next-page, even-page,
odd-page and continuous starts; page numbering that restarts and counts in the scheme the
section chose. Headers and footers for the first page, for even pages and for the rest, with
"link to previous" followed back through the sections. A header taller than the top margin
pushes the body down rather than being drawn over it.

In WordprocessingML, an intermediate `sectPr` belongs to the section it closes while its `w:type`
states how the following section starts. The loader shifts that start forward and the saver writes
it back on the preceding `sectPr` without discarding an empty paragraph that may be meaningful to
the document. The model marks such a carrier, and PDF pagination does not charge its empty paragraph
mark as an extra body line. Saved `w:lastRenderedPageBreak` hints are honoured by default,
including hints inside table cells where the whole row moves. Set
`PdfExportOptions.HonorLastRenderedPageBreaks = false` after substantial editing when reflow is
preferred over matching the pagination of the last Word render.

**Columns.** Text fills the first column to the bottom, then the next, then the next page.
Equal columns split by count and gap; explicit ones sit at the widths the section states. A
column break moves to the top of the next column — in a one-column section that is the next
page, which is what Word does with it too. The separator, when asked for, is ruled down the
middle of each gap for the height the content reached. Footnotes stay full-width under the
columns, and headers and footers never column at all. Between columns of unequal width a
paragraph moves whole, because its lines were measured against the column it started in.

A continuous section stacks a new band of columns below the old one on the same page, and
balances the section it ends (ISO/IEC 29500-1 §17.18.5): the placement — never the measuring,
so no counter moves — is rewound to the top of the band and played again against an even share
of the content's height. A section that spilled past one page, or that carries footnotes or a
table, keeps the columns it filled, which is never wrong, only uneven.

**Notes.** A footnote prints a superscript mark in the text and its body at the foot of the page
that mark landed on, under a rule. That is circular — the room left for the text depends on how
much the notes took, and which notes those are depends on how much text fitted — so the page is
filled a line at a time, and a line that owes a note has to find room for the note as well as for
itself. One that cannot takes its note to the next page. Endnotes are collected after everything
else. Numbering follows `w:footnotePr` and `w:endnotePr`, from the settings part or from a section
that overrides it: the scheme, the number to start at, and whether the count restarts each section.

**Equations.** An `m:oMath` is laid out in two dimensions against the same font metrics as the
text around it: a fraction stacks its numerator over a ruled bar, a script sits at the corner of
its base at two-thirds of the size, a radical grows its sign to cover the radicand and draws a
vinculum over it,
brackets grow to the height of what they hold, a matrix and an array line up in a grid, and a
sum carries its limits over and under while an integral carries them at its corners. Letters
lean and digits do not, which is the convention mathematics has always used and what a run's
`m:sty` or `m:nor` overrides. A phantom takes up its room without being drawn. A display
paragraph holding several equations stacks them.

A mathematical font carries stretchy variants of every bracket and a table of how far to raise
each script; none of that can be assumed of the fonts a document names, so a bracket that has to
grow is drawn at a larger size and the offsets are the proportions Word uses.

**Charts.** A chart is drawn from the numbers the document cached for it: bars, lines, areas and
scatters against a value axis ruled and labelled at round numbers, and pies and doughnuts as
wedges, in the accent colours Office uses, with the title over the plot and a legend under it.
Every other kind keeps its space and says in the diagnostics that it was not drawn — a surface
plot drawn as a bar chart would be worse than nothing. Nothing about the chart part's own
styling is read.

**Fields.** `PAGE`, `NUMPAGES`, `SECTIONPAGES` and `PAGEREF` are computed against the
pagination the export itself produced; every other field prints the result the producer
cached. Field instructions never print. `PAGEREF` is what a Word-built table of contents
prints its numbers with, so the TOC in the PDF shows where headings actually landed — the
entry list itself is still the cached one, because which headings a TOC lists is a field Word
recomputes, not a layout question. A `PAGEREF` whose bookmark is nowhere prints the error Word
prints and is named in the diagnostics; the `\p` form ("above", "below") keeps its cache.
`PdfExportOptions.UpdatePageFields` turns all of this off, printing every cache as Word left
it. A blind first estimate of a `PAGEREF` triggers the same single recomposition an unstable
`NUMPAGES` does, so the widths the lines were measured with match what is printed.

**Links.** A link out of the document becomes a URI annotation. A link wrapping across several
lines uses one annotation per page with a quadrilateral for each visual segment, rather than
duplicating the link in the review/accessibility structure. A link to a bookmark becomes a
destination pointing at the place the bookmark landed, resolved after pagination; one whose
bookmark is nowhere is left inert rather than pointed at the wrong page.

**Pictures.** JPEG and PNG are embedded without being re-encoded. A bitmap, a GIF or a TIFF is
decoded and written back out deflated, which loses nothing but is no longer the bytes the
document held, so it is named in the diagnostics as `ImageTranscoded`. A metafile is not drawn —
that would need a graphics device this converter does not have — but the very common metafile
that wraps a bitmap rather than drawing anything has the bitmap taken out of it and embedded;
one that really draws is reported and its space left blank. Inline pictures take room on
the line; floating ones are placed against the page, the margin or their paragraph, at the offset
or the edge their anchor names. The anchor is read from either of the two ways a document can
state it, so a file converted out of `.doc` positions its pictures as exactly as one Word saved.

| Format | What happens |
| --- | --- |
| JPEG, PNG | Embedded untouched, compression and palette intact |
| BMP | Decoded: 1 to 32 bits, palette, bit fields, both run-length encodings, either way up |
| GIF | First frame decoded, its interlace undone and its transparent index kept as a mask |
| TIFF | First image decoded: uncompressed, LZW, PackBits and deflate, in grey, palette or RGB |
| EMF, WMF | The bitmap inside is taken out; a metafile that draws is reported, not approximated |
| Anything else | Reported as `ImageSkipped` |

**Wrapping.** Text keeps out of a floating object, grown by the clearances the anchor asks for.
Square wrapping narrows the lines beside the object; tight and through wrapping follow the
object's own wrapping polygon (`wp:wrapPolygon`), each line fitted against the polygon's extent
at its own height — which is why text steps in under a slanted edge. Text allowed on both sides
of a float runs down both: the line continues on the far side of the object at the same height,
each side aligned and justified on its own. Top-and-bottom wrapping empties the whole band and
the text continues below. A paragraph is measured a second time once the composer knows
something floats over the spot it starts at — and only measured, never recounted: its list
marker and its footnotes are replayed from the first measurement, so nothing is numbered twice.

**Vertical text.** A table cell whose text flows `tbRl` or `btLr` turns its words on their side:
the lines run along the cell's height — which the row grows to fit — and stack across its
width. A text box does the same under `bodyPr@vert`. What stacks past the width is cut off at
the edge, and the cut is said in the diagnostics.

**Right-to-left.** A `w:bidi` paragraph is laid out from the right: text travels the pipeline
in logical order and is broken into lines that way, and only a finished line is rearranged into
visual order — Hebrew and Arabic runs reversed with their brackets mirrored, Latin and numbers
keeping their own direction inside them. Arabic is joined into its letterforms — initial,
medial, final, and the lam-alef ligature — using the Unicode presentation forms, so any font
that carries Arabic carries them. A right-to-left run takes the complex-script font, weight and
size the format names for it, and a `bidiVisual` table draws its first column at its right edge.

**Text boxes.** A shape with words in it is drawn from the geometry the model reads off its
markup: the fill, the frame, and the content — paragraphs and tables alike — laid out against
the box's own width, behind the `wps:bodyPr` insets (or Word's usual defaults when they are
omitted). Generated zero-inset fixed-layout boxes therefore use their whole frame instead of
being measured against an invented margin. An inline box stands on the baseline and takes
room on its line; a floating one is placed by its anchor and the text wraps round it like round
a picture. Boxes nest. A box is a small page that never turns: content taller than it is cut off
at its bottom edge, and the cut is said in the diagnostics — as is a shape whose markup states
no size, which cannot be drawn at all.

WordprocessingShape straight connectors, including the modern branch of a legacy VML fallback,
are read as line primitives rather than opaque raw markup. Generated fixed-layout text boxes and
lines use Word-compatible anchored DrawingML, so they survive a save/load round trip and render in
desktop Word as well as in Quillwright.Pdf.

## Interactive comments

An ordinary PDF export matches printing and omits review balloons, reporting one `ContentSkipped`
warning. Opt in when the PDF is meant to remain a review document:

```csharp
var options = new PdfExportOptions { IncludeComments = true };
document.SaveAsPdf("review.pdf", options);
```

Each top-level Word comment becomes a sticky-note annotation at its laid-out comment reference.
The body, author, UTC date and durable id are retained; Word replies become PDF replies, and the
`done` flag is retained on each individual message as an ordinary informational reply. Word does
not record who resolved a message or when, while ISO 32000-1 §12.5.6.3 requires the `/T` entry of
a `/State` reply to identify that user. The exporter therefore does not fabricate a resolver or
write an anonymous machine-readable `Completed` state: the informational reply has no author,
timestamp, `/State` or `/StateModel`, and a `comment-resolved-state` diagnostic reports the loss.
A top-level intelligent-placeholder follow-up is labelled `Follow-up` instead of exposing the
producer prompt that [MS-DOCX] says to ignore.

The icon is interactive viewer content, not page text, so it has zero layout metrics and does not
change wrapping, empty-line height or pagination. Its bidi direction follows the logical run whose
range ends there. A model comment with no printable anchor is skipped with a `comment-anchors`
diagnostic; a reference whose comment body is missing uses `comment-references`.
References inside repeated header/footer furniture or rotated text are not duplicated onto pages;
their model messages remain un-emitted and are reported by the same `comment-anchors` diagnostic.

When `Tagged` is also enabled, every note, reply and review-state annotation is owned by an `Annot`
structure element through the full `OBJR`/`StructParent`/`ParentTree` chain. The `Annot` is inserted
in `/K` where the zero-width comment reference occurs, between the marked-content sequences before
and after it; this preserves assistive reading order rather than merely linking the object in both
directions. Hyperlinks likewise use a `Link` element that owns both their marked text and their link
annotation. This is built before any caller declares PDF/UA; pop-up helper annotations are
intentionally excluded, as ISO 14289 does not treat them as page content.

## Tagged PDF

`PdfExportOptions.Tagged` builds a structure tree as the pages are drawn:

| Word | PDF |
| --- | --- |
| Paragraph | `P` |
| Outline level 1–9 | `H1` to `H6` |
| List item | `L` > `LI` > `LBody` |
| Table | `Table` > `TR` > `TH` or `TD` |
| Picture | `Figure`, with the description as `/Alt` |

Header cells are given a column scope. Interactive comments use `Annot`; hyperlinks use `Link`.
Headers, footers, shading, rules and borders are marked as artifacts, so every mark on the page
either belongs to the tree or says that it belongs to nobody. The document language comes from
`PdfExportOptions.Language`, the document properties or the default run formatting, and a tagged
document with a title asks readers to show it.

A document built this way passes Inkwright's PDF/UA-1 validator without violations, but the
converter does not claim conformance on its own: alt text, reading order and colour contrast are
the author's to get right. Claim it explicitly when you have checked:

```csharp
PdfUaProfile.Declare(pdf, PdfUaConformance.Ua1, title);
```

## What is not laid out

These are refusals rather than omissions: each one is reported in the diagnostics rather than
being approximated into something that looks right and is not.

- **Wrapping through an outline's hollows.** Through wrapping keeps text out of the polygon's
  full extent at each height, as tight wrapping does, rather than letting it into concavities.
- **A tight wrap without a polygon** — the VML branch carries none — keeps clear of the
  object's rectangle instead.
- **Wrapping inside table cells.** A float anchored in a cell is drawn where it belongs, but the
  cell's text does not flow round it.
- **A wrapped paragraph carried to another page.** The lines keep the widths they were measured
  with beside floats that stayed behind, which errs towards narrower, never wider.
- **Balancing a section that spilled past one page**, or one carrying footnotes or a table: it
  keeps the columns it filled.
- **Notes numbered afresh on every page**, which is circular in a way the rest is not: the number
  is measured before the page it lands on is known. Such notes are numbered straight through.
- **Endnotes collected per section.** They are collected at the end of the document instead.
- **Splitting a single note across pages.** A note moves whole; one taller than a page of its own
  runs past the margin.
- **A note first referenced inside a table row** reserves its room for the whole row rather than
  for the part of the row that fits, so a split row leaves a little more space than it needed.
- **Content taller than its text box.** The box does not grow; the content is cut off at its
  bottom edge, the way Word cuts it when the box is not allowed to grow.
- **Shapes whose markup states no size.**
- **A chart of a kind that cannot be drawn from cached values** — bubble, radar, surface and
  stock — and every chart's own styling: its fills, its gradients, its data labels.
- **A floating chart**, which is drawn where it is anchored rather than where it floats.
- **SmartArt.**
- **Metafiles that draw rather than wrap a bitmap.** Lines, curves and text in an EMF or a WMF
  would need a graphics device to execute; only an embedded bitmap is recovered.
- **The fax encodings and JPEG-in-TIFF**, and a TIFF whose planes are stored one after another
  rather than interleaved.
- **The stylistic shaping a font's own substitution table carries.** Arabic joins through the
  Unicode presentation forms — the classical shapes — rather than through `GSUB`, so a font's
  stylistic alternates stay unused, and a font without the presentation forms shows the
  unjoined letters.
- **Rotated East Asian flows** (`tbRlV`, `tbLrV`, `lrTbV`) and whole vertical sections are laid
  out the ordinary way; tables inside turned cells are not drawn.

## Costs

BenchmarkDotNet 0.15.8, .NET 10, Windows Server 2022, Intel Core i7-8700; three warmup and ten
measured iterations.

| | Pages | Mean | Allocated |
| --- | ---: | ---: | ---: |
| Justified prose, 2 000 paragraphs | 80 | 188 ms | 96.8 MB |
| The same, tagged | 80 | 197 ms | 103.6 MB |
| A table of 500 rows by 4 columns | 10 | 66 ms | 34.7 MB |

That is a little over two milliseconds a page, and tagging costs about five per cent. The
allocation is measurement rather than output: every line is measured against real font metrics,
and a paragraph is measured once however many pages it ends up spanning.

```bash
dotnet run -c Release --project benchmarks/Quillwright.Benchmarks -- --filter *PdfBenchmarks*
```

## How it is put together

Rendering is two passes over a seam. Composition walks the model, measures it and decides what
lands where; rendering turns that decision into content streams. Keeping them apart is what lets
a page be thrown away and laid out again, which is what a document with a `NUMPAGES` field needs:
the count of pages is not known until the pages exist, so the document is composed, counted, and
composed once more if the count it assumed while measuring turned out to be a different width.

```
InlineWalker      a paragraph → what actually prints, fields and tracked deletions resolved
LineBreaker       those pieces → lines, with tabs settled, kept out of whatever floats over them
ParagraphLayouter lines → heights, indents, alignment, the spacing around the paragraph
TableLayouter     a table → column widths, cell content, row heights
PageComposer      measured blocks → columns and pages, honouring breaks, keeps and widow control
PageRenderer      pages → content streams, annotations and the structure tree
```

Everything above the composer works in points with the origin at the top-left corner, the way
Word thinks. The flip to PDF's bottom-left origin happens in one place, `PageGeometry.ToPdfY`.
