# Importing HTML

`HtmlImporter.Import` turns HTML — a full page or a fragment — into a `WordDocument`. The
mapping mirrors [the exporter's](html-export.md), construct for construct, so a page exported from a document imports back with the same headings, lists, tables and
formatting; and it is
built for the HTML that editors, exporters and language models actually produce, including the
`mso-` styles and `<o:p>` wrappers Word's own HTML carries, which are stepped over.

```csharp
HtmlImportResult result = HtmlImporter.Import(html);
await result.Document.SaveAsync("imported.docx");
foreach (HtmlImportWarning warning in result.Diagnostics)
    Console.WriteLine(warning);

// Or from a file, with images resolved beside it
HtmlImportResult fromFile = await HtmlImporter.ImportFileAsync("page.html");
```

## The mapping

| HTML | Document |
| --- | --- |
| `h1`–`h6` | `Heading1`–`Heading6` |
| `p`, `div` and the other flow containers | Paragraphs; unclosed `p` and `li` close the way browsers close them |
| `strong`/`b`, `em`/`i`, `u`, `s`/`strike`, `sup`, `sub` | The run formatting they name |
| `code`, `tt`, `kbd`, `samp`, `pre` | Consolas; `pre` keeps its line breaks in a `CodeBlock` paragraph |
| `mark` | Yellow highlighting |
| `ins`, `del` | Underline and strikethrough, with a diagnostic — formatting, not a fabricated tracked change |
| Inline CSS: `color`, `background`, `font-weight/style/size/family/variant`, `text-decoration`, `text-align` | The formatting Word can also say; `mso-*` properties are ignored |
| `a href` | A real `Hyperlink`; `#fragment` becomes an anchor; `a id`/`name` becomes a bookmark |
| `ul`/`ol`/`li`, nested | Real numbering instances, level per depth, on `ListParagraph` |
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

## The parser

The parser is the library's own and implements the HTML standard's parsing algorithm
([WHATWG HTML §13.2](https://html.spec.whatwg.org/multipage/parsing.html)) rather than
approximating it: all 84 tokenizer states — including the six that untangle a `script`
element's escaped and double-escaped content, RCDATA and RAWTEXT with their appropriate end
tag rule, the comment, doctype and CDATA states — and all 21 insertion modes of the tree
builder, with

- the **stack of open elements** and its five scopes,
- the **list of active formatting elements** with the Noah's Ark clause, which is what makes
  `<b>one<p>two` put the second paragraph's text in bold too,
- the **adoption agency algorithm**, so `<p>1<b>2<i>3</b>4</i>5` comes out the way the
  standard's own worked example says it does,
- **foster parenting**, so content stranded in a table lands in front of it rather than
  inside,
- implied end tags, the quirks-mode doctype list, and all **2229 named character
  references** of §13.5 with the legacy semicolon rule.

The parser runs with scripting disabled — a document importer is not a browser — so a
`noscript` element's content is parsed as the markup it is, exactly as in a browser with
scripting turned off. Parse errors are not reported: the standard pairs each one with the
recovery it requires, and it is the recovery, faithfully performed, that decides what tree an
author's markup produces.

Conformance is not self-assessed. Beside the tests taken from the standard's own worked
examples, ninety-five cases are checked against **Chrome's own parser** — the expected tree
for each was read out of a real browser through the DevTools protocol — covering script
escaping, character reference edge cases, misnested formatting, nested and malformed tables,
foreign content, templates, frameset and quirks mode.
