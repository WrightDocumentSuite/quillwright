# Importing Markdown

`MarkdownImporter.Import` turns Markdown into a `WordDocument`: CommonMark blocks and
inlines, and the GitHub extensions — tables, strikethrough and task lists. The
mapping shares a semantic subset with [the exporter](markdown-export.md). Constructs in the
table below map back to the styles the exporter recognises, but export-only raw-HTML fallbacks
are not interpreted and this is not a general round-trip guarantee.

```csharp
MarkdownImportResult result = MarkdownImporter.Import(File.ReadAllText("report.md"));
await result.Document.SaveAsync("report.docx");
foreach (MarkdownImportWarning warning in result.Diagnostics)
    Console.WriteLine(warning);

// Or from a file, with images resolved beside it
MarkdownImportResult fromFile = await MarkdownImporter.ImportFileAsync("report.md");
```

`MarkdownImportOptions.Budget` bounds encoded file bytes, decoded characters and lines,
block/inline nodes, quote/list/inline nesting, and imported images. The file overload checks bytes
before decoding, and data URI sizes before base64 allocation. A resource breach throws
`DocumentLoadLimitException`; syntax approximations remain in `Diagnostics`. The common policy
and defaults are in [loading-untrusted-input.md](loading-untrusted-input.md).

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

The style names are the ones the exporter recognises, so supported constructs map both ways:
`Heading1` comes back as `#`, `Quote` as `>`, `CodeBlock` as a fence, and a monospace run as
backticks.

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

Local image references are percent-decoded once and must be portable relative paths. Rooted
paths, empty, `.` or `..` segments, malformed escapes, query/fragment syntax and Windows device
names are not opened. Every existing path component below `MediaDirectory` is checked before
the file is opened; a symbolic link, junction or other reparse point makes the image
`ImageSkipped`, even when its target would remain inside the directory. The configured
`MediaDirectory` itself is the caller's trust boundary and must not be concurrently replaced or
modified by an attacker. For an attacker-writable media tree, set `ImportImages = false`.

The parser is the library's own, not a dependency: the CommonMark constructs above with the
delimiter-run emphasis algorithm, reference definitions collected before parsing, and the
laziness rules simplified where a word processor gives the distinction nothing to land on —
a lazy continuation line joins its paragraph, but a paragraph interrupted by any block marker
ends.
