# Importing HTML

`HtmlImporter.Import` turns an HTML document into a `WordDocument`; fragment-like input is
recovered into a document tree by the ordinary HTML document parser. Use
`HtmlImporter.ImportFragment` when the caller has a real fragment and knows the element whose
`innerHTML` it represents. The mapping shares the supported constructs listed below with
[the exporter](html-export.md), including headings, lists, tables and formatting. It does not
yet reconstruct every exported construct, notably notes, so it is a semantic conversion rather
than a general round-trip guarantee. The importer is built for HTML that editors, exporters and
language models actually produce, including the
`mso-` styles and `<o:p>` wrappers Word's own HTML carries, which are stepped over.

```csharp
HtmlImportResult result = HtmlImporter.Import(html);
await result.Document.SaveAsync("imported.docx");
foreach (HtmlImportWarning warning in result.Diagnostics)
    Console.WriteLine(warning);

// Or from a file, with images resolved beside it
HtmlImportResult fromFile = await HtmlImporter.ImportFileAsync("page.html");

// Parse as textarea.innerHTML: tags are text in this context.
HtmlImportResult fragment = HtmlImporter.ImportFragment(
    "literal <b>markup</b> &amp; text",
    contextElement: "textarea");
```

`ImportFragment` implements the standard's context-sensitive fragment algorithm rather than
wrapping the string in a synthetic `body`. The context selects the initial tokenizer state
(`textarea` is RCDATA, `style` is RAWTEXT, and so on), the insertion mode (`table`, `tr`,
`template`, …), and the foreign-content rules. The context element is not itself imported.
Scripting remains disabled, so a `noscript` fragment is parsed as markup.

`ImportFileAsync` first reads the file as bytes. A UTF-8 or UTF-16 BOM is certain and takes
precedence; without one, the importer runs the WHATWG byte prescan over the first 1024 bytes,
including `meta charset` and the `http-equiv=content-type` form. The scan ignores declarations
inside comments and attributes, applies the standard's label remaps (including
`x-user-defined` to windows-1252 and an in-document UTF-16 label to UTF-8), and does not enable
prohibited encodings such as UTF-7. If no supported declaration is found, UTF-8 is used. This
bounded prescan is intentional: a later `meta` declaration cannot retroactively select the
file encoding.

`HtmlImportOptions.Budget` bounds encoded file bytes, decoded characters and lines, parser nodes
and depth, and both individual and total image payloads. File lengths and local image lengths
are checked before allocation; a base64 data URI is size-checked before decoding. A breach throws
`DocumentLoadLimitException`, not an import diagnostic. See
[loading-untrusted-input.md](loading-untrusted-input.md) for a shared DOCX/DOC/HTML/Markdown/RTF
policy.

## The mapping

| HTML | Document |
| --- | --- |
| `h1`–`h6` | `Heading1`–`Heading6` |
| `p`, `div` and the other flow containers | Paragraphs; unclosed `p` and `li` close the way browsers close them |
| `strong`/`b`, `em`/`i`, `u`, `s`/`strike`, `sup`, `sub` | The run formatting they name |
| `code`, `tt`, `kbd`, `samp`, `pre` | Consolas; `pre` keeps its line breaks in a `CodeBlock` paragraph |
| `mark` | Yellow highlighting |
| `ins`, `del` | Underline and strikethrough, with a diagnostic — formatting, not a fabricated tracked change |
| Inline CSS: `color`, `background`, `font-weight/style/size/family/variant`, `text-decoration`, `text-align`, list `list-style-type` | The formatting Word can also say; `mso-*` properties are ignored |
| `a href` | A real `Hyperlink`; `#fragment` becomes an anchor; `a id`/`name` becomes a bookmark |
| `ul`/`ol`/`li`, nested | Real numbering instances on `ListParagraph`; every nested list keeps its own owner, marker kind and start, while `ol start`/`type`/`reversed`, `li type` and `li value` determine the actual counters |
| `table` with `thead`, `th`, `colspan`, `rowspan` | A real table on `TableGrid`: header rows, grid spans, vertical merges with their continuation cells |
| `img` | The image embedded from a base64 `data:` URI or from `MediaDirectory`; otherwise its alt text, with a diagnostic; `width`/`height` in pixels or CSS set the size |
| `br`, `hr` | A line break; a bottom-ruled paragraph |
| Entities | The characters they name: the common names and every numeric form |
| `<title>` | `document.Properties.Title` |

Whitespace collapses the way browsers collapse it, except inside `pre`. A `script`, `style`,
`form`, `iframe` or media element has no document counterpart and is left out with a
diagnostic naming it and its line; an element the importer does not model is unwrapped around its content rather than dropped,
with a diagnostic naming it as well. A remote image is never fetched — nothing in
this library opens a network connection — and a relative path is resolved only inside
`MediaDirectory`.

An inline CSS `list-style-type` overrides the older HTML `type` hint and is inherited through
flow containers and list items. The Word-compatible subset is `decimal`,
`decimal-leading-zero`, roman and latin numbering, `disc`, `circle`, `square` and `none`;
escaped CSS identifiers are decoded before matching. A declaration on an individual `li`
overrides only that item's marker. HTML integer attributes follow the browser parser: leading
ASCII whitespace and a sign are accepted, the initial run of digits is used, and a non-ASCII
space does not count as whitespace.

Inline declaration parsing keeps CSS2 token boundaries: comments and semicolons inside quoted
strings do not split declarations, identifier and string escapes are decoded, and
`!important` participates in cascade order. A `font-family` list is validated before its first
family is used; font-name case and non-ASCII identifier characters such as NBSP are preserved.

Only the first paragraph owned by an `li` carries Word numbering. Further paragraphs use the
`ListParagraph` style with an explicit continuation indent and no `NumberingId`; this survives
a DOCX save/load and lets export rebuild one `li` around multiple paragraphs, trailing content
and sibling nested lists. Empty items receive an empty numbered paragraph so that they still
consume an ordinal. Descending ordered lists use ordinary Word numbering restarts, so saving
and exporting again keeps the displayed values even when the equivalent HTML uses `start` plus
`li value` rather than retaining the original `reversed` spelling.

Numbering identifiers are allocated by one builder for the whole import. A reversed list or a
sequence of explicit, non-consecutive `li value` attributes still needs one Word numbering
instance per restart — that is part of the saved document — but creating those instances does
not repeatedly scan the ones already created. An operation-wide index is likewise built when
the document is exported again, including after a DOCX reload, so restart-heavy and deeply
nested lists scale with their items and emitted numbering records rather than with their square.

Local image references are percent-decoded once and must be portable relative paths. Rooted
paths, empty, `.` or `..` segments, malformed escapes, query/fragment syntax and Windows device
names are not opened. Every existing path component below `MediaDirectory` is checked before
the file is opened; a symbolic link, junction or other reparse point makes the image
`ImageSkipped`, even when its target would remain inside the directory. The configured
`MediaDirectory` itself is the caller's trust boundary and must not be concurrently replaced or
modified by an attacker. For an attacker-writable media tree, set `ImportImages = false`.

## The parser

The parser is the library's own and implements the HTML standard's parsing algorithm
([WHATWG HTML §13.2](https://html.spec.whatwg.org/multipage/parsing.html)) rather than
approximating it: all 84 tokenizer states — including the six that untangle a `script`
element's escaped and double-escaped content, RCDATA and RAWTEXT with their appropriate end
tag rule, the comment, doctype, CDATA and processing-instruction states — and all 21 insertion
modes of the tree builder. Document types and processing instructions are retained in the
parsed document tree. Tree construction includes

- the **stack of open elements** and its five scopes,
- the **list of active formatting elements** with the Noah's Ark clause, which is what makes
  `<b>one<p>two` put the second paragraph's text in bold too,
- the **adoption agency algorithm**, so `<p>1<b>2<i>3</b>4</i>5` comes out the way the
  standard's own worked example says it does,
- **foster parenting**, so content stranded in a table lands in front of it rather than
  inside,
- implied end tags, the quirks-mode doctype list, and all **2229 named character
  references** of §13.5 with the legacy semicolon rule.

The doctype decision distinguishes full quirks from the XHTML Transitional/Frameset
identifiers that require only limited quirks; those identifiers therefore do not trigger the
full-quirks paragraph/table recovery rule.

The same tokenizer and tree builder also run the standard's fragment setup: a separate
fragment insertion target, the adjusted current node, context-sensitive reset of the
insertion mode, template insertion-mode stack initialization, and SVG/MathML namespace
inheritance. This is why CDATA is text in an SVG fragment but bogus-comment syntax in an HTML
fragment, and why parsing a `tr` in a `table` context creates the browser-implied `tbody`.

The parser runs with scripting disabled — a document importer is not a browser — so a
`noscript` element's content is parsed as the markup it is, exactly as in a browser with
scripting turned off. Parse errors are not reported: the standard pairs each one with the
recovery it requires, and it is the recovery, faithfully performed, that decides what tree an
author's markup produces.

No non-standard length cap is imposed on a tag's attributes or on DOCTYPE public and system
identifiers. Instead, duplicate attribute names are indexed once per tag (ordinal comparison,
first value wins), and identifiers are accumulated in append-only buffers. Parsed elements keep
the source-order list alongside the same kind of name index, so Noah's Ark comparisons and the
required merge of repeated `html`/`body` tags do not reintroduce pairwise scans. This preserves
the standard's recovery semantics — including missing versus empty DOCTYPE identifiers — while
keeping these attacker-controlled shapes linear in their input size, with proportional memory.

Conformance is not self-assessed. Beside the tests taken from the standard's own worked
examples, ninety-five cases are checked against **Chrome's own parser** — the expected tree
for each was read out of a real browser through the DevTools protocol — covering script
escaping, character reference edge cases, misnested formatting, nested and malformed tables,
foreign content, templates, frameset and quirks mode.
