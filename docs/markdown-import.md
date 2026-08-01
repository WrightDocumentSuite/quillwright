# Importing Markdown

`MarkdownImporter.Import` turns Markdown into a `WordDocument`: CommonMark blocks and
inlines, and the GitHub extensions — tables, strikethrough and task lists. The
mapping is the inverse of [the exporter's](markdown-export.md), construct for construct, so a
document that came from Markdown exports back to the Markdown it was written with — as far as
the two formats overlap, which is the only claim either direction makes.

```csharp
MarkdownImportResult result = MarkdownImporter.Import(File.ReadAllText("report.md"));
await result.Document.SaveAsync("report.docx");
foreach (MarkdownImportWarning warning in result.Diagnostics)
    Console.WriteLine(warning);

// Or from a file, with images resolved beside it
MarkdownImportResult fromFile = await MarkdownImporter.ImportFileAsync("report.md");
```

## The mapping

| Markdown | Document |
| --- | --- |
| `#`–`######`, setext underlines | `Heading1`–`Heading6` |
| Paragraphs; two trailing spaces or `\` | Paragraphs; a line break inside one |
| `>` quotes | The `Quote` style |
| Fenced and indented code blocks | A `CodeBlock` paragraph in Consolas, line breaks kept |
| `` `code` `` | A run in Consolas |
| `-`, `*`, `+`, `1.` lists, nested | Real numbering instances, one per top-level list, level per depth, on `ListParagraph` |
| `- [ ]`, `- [x]` task items | The list item with `☐` or `☒` opening it |
| `**bold**`, `*italic*`, `~~strike~~` | Bold, italic and strikethrough runs, nesting included |
| `[text](url "title")`, `[ref][]`, `<autolink>` | A real `Hyperlink` range; a `#fragment` target becomes an anchor |
| `![alt](path)` | The image embedded from `MediaDirectory` or a base64 `data:` URI; otherwise the alt text, with a diagnostic |
| GFM tables with `:---:` alignment | A real table on `TableGrid`: a repeating bold header row, per-column alignment, `\|` escapes |
| `---` thematic break | An empty paragraph ruled along its bottom border |
| Entities and escapes | The characters they name |

The style names are the ones the exporter recognises, which is what makes the two directions
inverses: `Heading1` comes back as `#`, `Quote` as `>`, `CodeBlock` as a fence, a monospace
run as backticks.

## What is approximated, and how it says so

An import never throws for syntax it cannot honour; it imports what it can and names the rest
in `MarkdownImportResult.Diagnostics`, each entry with its source line.

- **Raw HTML** has no interpreter here and is kept as the text it is (`HtmlKeptAsText`).
- **A front-matter block** is metadata for another tool and is skipped (`UnsupportedSyntax`).
- **A remote image** is not fetched — nothing in this library opens a network connection — and
  a relative path outside `MediaDirectory`, or with no directory given, cannot be resolved;
  in every such case the alt text stands in and the diagnostic names the path
  (`ImageSkipped`).
- **An ordered list starting past 1** keeps Word's own numbering from 1; the start override is
  not carried.

The parser is the library's own, not a dependency: the CommonMark constructs above with the
delimiter-run emphasis algorithm, reference definitions collected before parsing, and the
laziness rules simplified where a word processor gives the distinction nothing to land on —
a lazy continuation line joins its paragraph, but a paragraph interrupted by any block marker
ends.
