# Exporting to Markdown

Quillwright projects the semantic content of a Word document into deterministic Markdown. The
renderer is part of the core `Quillwright` package: it does not paginate, invoke Word, write a
temporary `.docx`, or require another dependency.

## Quick start

`ToMarkdown` is a pure in-memory operation. It returns the text, every referenced sidecar image,
and diagnostics describing deliberate approximations:

```csharp
using Quillwright.Markdown;
using Quillwright.Model;

WordDocument document = await WordDocument.LoadAsync("report.docx");
MarkdownDocument markdown = document.ToMarkdown();

Console.Write(markdown.Text);
foreach (MarkdownExportWarning warning in markdown.Diagnostics)
    Console.Error.WriteLine(warning);

await markdown.SaveAsync("report-markdown");
// report-markdown/document.md
// report-markdown/media/image1.png, when referenced
```

`ExportMarkdownAsync` combines rendering and saving when the intermediate result is not needed:

```csharp
await document.ExportMarkdownAsync("report-markdown");
```

## A complete small example

This example is also a golden test. The test extracts the marked output fence below, so a change to
the renderer cannot silently leave the documentation behind.

```csharp
WordDocument document = WordDocument.Create();
document.Styles.GetOrAdd("Heading1");
document.Sections[0].AddParagraph("Release notes", "Heading1");

Paragraph intro = document.Sections[0].AddParagraph();
intro.AppendText("Quillwright ", RunFormat.Default);
intro.AppendText("exports", RunFormat.Default with { Bold = true });
intro.AppendText(" Word documents.", RunFormat.Default);

int bullets = document.Numbering.AddBulletList();
AddListItem("Preserves document order");
AddListItem("Returns sidecar images");

var table = new Table();
TableRow header = table.AddRow("Feature", "Result");
header.Format = header.Format with { IsHeader = true };
table.AddRow("Tables", "GFM or HTML");
document.Sections[0].Blocks.Add(table);

MarkdownDocument markdown = document.ToMarkdown();

void AddListItem(string text)
{
    Paragraph item = document.Sections[0].AddParagraph(text);
    item.Format = item.Format with { NumberingId = bullets, NumberingLevel = 0 };
}
```

<!-- expected-output:start -->
```markdown
# Release notes

Quillwright **exports** Word documents.

- Preserves document order
- Returns sidecar images

| Feature | Result |
| --- | --- |
| Tables | GFM or HTML |
```
<!-- expected-output:end -->

## Result and determinism

`MarkdownDocument.Text` always uses LF line endings and exactly one final LF. Two renders of the
same model and options produce the same text, diagnostics order, image order, and generated names.
Rendering does not accept or reject revisions and does not modify text, ranges, objects, styles, or
media in the source document.

`MarkdownDocument.Images` is ordered by first visible reference. Each `MarkdownImage` carries a
safe generated file name, the original encoded bytes, and its MIME type. Equal image bytes are
deduplicated with SHA-256 even when the document contains different `ImageData` instances.

## Dialects

`MarkdownFlavor.GitHub` is the default. It permits CommonMark, GFM pipe tables and strikethrough,
plus GitHub's platform footnote syntax. Footnotes are a GitHub feature, not part of the GFM
specification itself.

`MarkdownFlavor.CommonMark` does not generate GFM-only constructs. Tables and notes become
generated raw HTML, and strikethrough becomes `<del>`. Other targeted HTML fallbacks such as
`<ins>`, `<sub>`, `<sup>`, bookmark anchors, and sized images are used in either flavor when plain
Markdown cannot express the semantics.

## Word-to-Markdown mapping

- Ordinary paragraphs remain in document order and are separated by one blank line. Empty layout
  paragraphs collapse rather than imitating page spacing.
- Resolved outline levels become ATX headings. Levels deeper than six are clamped to `######` with
  a diagnostic.
- Consecutive `Quote` and `IntenseQuote` paragraphs form one block quote.
- Known code styles and paragraphs whose visible text is entirely monospaced become safe fenced
  code blocks. The fence is always longer than any matching run in the content.
- Word list definitions drive bullets, ordered-list starts, overrides, depth and restarts. A
  non-decimal Word marker keeps ordered-list semantics with a diagnostic because Markdown renderers
  choose the visible marker style.
- A simple table becomes a rectangular GFM pipe table in GitHub mode. Vertical merges, nested or
  multi-block cells, legacy horizontal merges, and all tables in CommonMark mode use generated HTML with
  encoded content and `rowspan`/`colspan` where applicable.
- Bold, italic, strikethrough, inline code, underline, subscript and superscript are preserved.
  Unsupported visual details such as font family, size, colour, highlighting and character effects
  are named in diagnostics rather than silently implying fidelity.

Tabs outside code become one space. Line breaks become hard Markdown breaks or `<br>` in table
cells. Page and column breaks become block boundaries because Markdown has no pagination.

## Fields and tracked changes

Complex and simple fields emit only their cached result; field instructions never leak into the
document. A field with no cached result is skipped with `ContentSkipped`.

`MarkdownRevisionMode.Accepted` shows inserted and moved-to content and hides deleted and moved-from
content. `Original` does the reverse. Deleted or inserted paragraph marks join paragraphs in the
corresponding view, and tracked table rows follow the same rule. The projection is non-mutating, so
the same `WordDocument` can be rendered in both modes.

## Links, bookmarks, pictures and notes

External and relative links are preserved with destination-specific escaping. `javascript`,
`vbscript`, and `data` targets are flattened to their visible label and reported as
`UnsafeLinkSkipped`. Word bookmarks receive safe unique HTML ids registered before body rendering,
so an internal link can point to a bookmark later in the document.

Pictures remain in reading order. Natural-size pictures use `![alt](...)`; resized pictures use a
generated `<img width="..." height="...">` when `PreserveImageDimensions` is enabled. Floating
geometry and wrapping cannot survive and are diagnosed. Browser-unfriendly formats such as TIFF,
EMF and WMF are returned unchanged with `MediaMayNotRender`, never silently transcoded.

Footnotes and endnotes are collected lazily in first-reference order and repeated references share
one definition. GitHub mode uses `[^fn-id]` and `[^en-id]`; CommonMark mode generates an encoded HTML
notes section. Missing note bodies leave a visible `?` marker and a diagnostic.

## Options

```csharp
var options = new MarkdownExportOptions
{
    Flavor = MarkdownFlavor.CommonMark,
    RevisionMode = MarkdownRevisionMode.Original,
    IncludePictures = true,
    IncludeHiddenText = false,
    PreserveImageDimensions = true,
    MediaDirectoryName = "assets/document images",
};

MarkdownDocument result = document.ToMarkdown(options);
```

`MediaDirectoryName` is a relative portable path. Empty segments, `.`, `..`, rooted paths, drive
paths, control characters and unsafe file-name characters are rejected before rendering. Spaces
are allowed and are percent-encoded in Markdown references while remaining ordinary directory names
on disk.

## Diagnostics

Diagnostics are deterministic and deduplicated by warning kind and stable subject. The kinds are:

- `FormattingDropped` — visible styling has no selected-dialect equivalent.
- `ContentSkipped` — content cannot be projected safely.
- `HtmlFallbackUsed` — generated HTML preserves a construct Markdown cannot express.
- `StructureApproximated` — content survived with a different structural representation.
- `UnsafeLinkSkipped` — an executable link target was removed.
- `MediaMayNotRender` — original image bytes are present, but browser support is uncommon.

Deliberate exclusions selected by the caller, such as `IncludePictures = false`, do not create
warnings.

## Saving contract

`SaveAsync(directory)` writes UTF-8 without a BOM to `document.md`, then writes referenced images
under `MediaDirectoryName`. It creates required directories and overwrites files it owns. It does
not delete stale media or unrelated files and it is not a transactional multi-file operation: a
cancellation or I/O error can leave files already written.

`ToMarkdown` itself performs no filesystem I/O. This separation lets a caller store the result in a
database, zip, HTTP response, or another naming scheme without first writing temporary files.

## Deliberate limits

- Headers and footers are page-dependent stories and are not mixed into body order.
- Comments, charts and embedded objects are not appended as synthetic body sections. Their
  presence is reported when they contain exportable-looking content.
- A compatibility block (`mc:AlternateContent`) is written as the branch a reader of this
  vocabulary shows, and the alternatives are not; that is reported as
  `StructureApproximated`.
- OMML equations are represented by linear extracted text, not converted to LaTeX or MathML.
- Text boxes are flattened at their anchor; shape geometry, floating position and wrapping are not
  representable.
- Markdown import and Markdown-to-Word round-trips are outside this exporter.

## Tests and specifications

Focused tests cover escaping, formatting boundaries, safe links, fields, all tracked range kinds,
headings, fences, list counters, logical table grids, HTML encoding, bookmarks, notes, image dedupe,
path traversal, UTF-8 output and deterministic rendering. The list counter and number formatter are
shared with the PDF renderer, whose existing numbering, note and page-field tests protect the shared
semantics.

The text rules follow [CommonMark 0.31.2](https://spec.commonmark.org/0.31.2/) and
[GitHub Flavored Markdown 0.29-gfm](https://github.github.com/gfm/). GitHub footnotes and supported
inline HTML elements are documented in GitHub's
[basic writing and formatting syntax](https://docs.github.com/en/get-started/writing-on-github/working-with-advanced-formatting/basic-writing-and-formatting-syntax#footnotes).
