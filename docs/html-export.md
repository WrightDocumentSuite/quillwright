# Exporting HTML

`document.ToHtml()` renders the main story as HTML for a web preview: by default one
self-contained page — doctype, a small neutral stylesheet, images embedded as `data:` URIs —
that opens in a browser with nothing beside it. The exporter shares its paragraph walker with
[the Markdown exporter](markdown-export.md), so the two agree about fields, tracked changes,
hidden text and note numbering; they differ in how much formatting the target can say, and
HTML can say almost all of it.

```csharp
HtmlDocument page = document.ToHtml();
await page.SaveAsync("preview");                       // preview/document.html, self-contained

HtmlDocument fragment = document.ToHtml(new HtmlExportOptions
{
    FullDocument = false,                              // a body fragment for a host page
    Images = HtmlImageMode.Sidecar,                    // media/image1.png beside the page
    RevisionMode = HtmlRevisionMode.Marked,            // tracked changes as <ins> and <del>
});
```

## The mapping

Semantic elements first, CSS only for what HTML has no element for:

| Document | HTML |
| --- | --- |
| Outline levels and `Heading1`–`Heading6` | `h1`–`h6` (deeper clamps to `h6`) |
| Bold, italic, strike, underline | `strong`, `em`, `s`, `u` — a shaped underline gets `text-decoration-style` |
| Superscript, subscript | `sup`, `sub` |
| A monospace face | `code` |
| Highlighting | `mark` with the palette colour |
| Colour, size, family, small caps, character shading | `span` with the CSS |
| Hyperlinks and bookmarks | `a href` (with `title`), `a id`; internal links resolve to the bookmark's id |
| Lists | Real nested `ul`/`ol` from the numbering: the kind per level, `type` for roman and letters, CSS2 marker styles where HTML has no `type`, and `start`/`value` where Word's counting departs from HTML's |
| Tables | `table` with `caption` from the accessible table caption, `thead`/`th` from header rows, `colspan` from grid spans, `rowspan` from vertical merges, cell shading and vertical alignment as CSS |
| Pictures | `img` with alt text and its stated size; `data:` URI or sidecar file |
| Footnotes and endnotes | Superscript links to a `footnotes` section at the foot, linked back |
| The `Quote` and code styles | `blockquote` and `pre` |
| A bottom-ruled empty paragraph | `hr` |
| Tracked changes in `Marked` mode | `ins` and `del` |

A nested list is emitted inside the `li` that owns it, never as a direct child of another
`ul` or `ol`. Counter restarts are kept with `start` on the list and `value` on the item, which
also gives a standards-valid representation of descending numbering. Unnumbered
`ListParagraph` paragraphs whose explicit indent matches an open list level are emitted as
continuation `p` elements inside that same item; this is the DOCX representation used by the
HTML importer for a multi-paragraph `li` and for content following a nested list.

The exporter snapshots numbering instances and definitions once for the operation. Resolving
paragraph styles, restart overrides and nested-list ownership therefore uses the same indexed
view instead of rescanning `numbering.xml` for every item. If a malformed document contains
duplicate numbering identifiers, the first declaration still wins, matching the public model's
existing resolution semantics.

Notes use the reciprocal-link pattern suggested for longer annotations by WHATWG HTML §4.14.4,
because HTML has no dedicated footnote element. `fn-<id>-<ordinal>` and
`en-<id>-<ordinal>` labels distinguish footnotes from
endnotes and carry the model id. Repeated references share one definition but receive distinct
HTML `id` values and distinct backlinks, so the page remains standards-valid. Note bodies may
contain several paragraphs and references to other notes; the exporter discovers that growing
set before it emits definitions, which also makes cyclic and self-references finite. This exact
generated shape is recognized by the HTML importer and reconstructs `Note`/`NoteReference`
objects rather than ordinary links and list items.

A link whose target could execute — `javascript:` and its relatives — is rendered as plain
text and named in the diagnostics (`UnsafeLinkSkipped`); text and attributes are always
escaped. What the walker cannot carry — a chart, raw OOXML — is left out with the same
diagnostics the Markdown export gives, and everything else about the export is deterministic:
the same document renders to the same bytes.

Comments and replies are review metadata rather than page content and are not embedded in the
HTML preview. If the source contains any, the exporter adds one `ContentSkipped` diagnostic
with subject `comments`; the anchored document text is still emitted normally.

## What it is not

A preview, not a page-layout engine: sections flow into one page, headers, footers and
floating positions are not reproduced, and pagination lives in
[`Quillwright.Pdf`](pdf-export.md). The stylesheet is deliberately small and neutral; a caller
who wants their own look exports a fragment (`FullDocument = false`) into their own page.
