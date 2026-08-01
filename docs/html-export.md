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
| Lists | Real nested `ul`/`ol` from the numbering: the kind per level, `type` for roman and letters, `start` and `value` where Word's counting departs from HTML's |
| Tables | `table` with `thead`/`th` from header rows, `colspan` from grid spans, `rowspan` from vertical merges, cell shading and vertical alignment as CSS |
| Pictures | `img` with alt text and its stated size; `data:` URI or sidecar file |
| Footnotes and endnotes | Superscript links to a `footnotes` section at the foot, linked back |
| The `Quote` and code styles | `blockquote` and `pre` |
| A bottom-ruled empty paragraph | `hr` |
| Tracked changes in `Marked` mode | `ins` and `del` |

A link whose target could execute — `javascript:` and its relatives — is rendered as plain
text and named in the diagnostics (`UnsafeLinkSkipped`); text and attributes are always
escaped. What the walker cannot carry — a chart, raw OOXML — is left out with the same
diagnostics the Markdown export gives, and everything else about the export is deterministic:
the same document renders to the same bytes.

## What it is not

A preview, not a page-layout engine: sections flow into one page, headers, footers and
floating positions are not reproduced, and pagination lives in
[`Quillwright.Pdf`](pdf-export.md). The stylesheet is deliberately small and neutral; a caller
who wants their own look exports a fragment (`FullDocument = false`) into their own page.
