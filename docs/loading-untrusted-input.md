# Loading untrusted input

Every public document importer applies a finite `DocumentLoadBudget` by default. The shared
budget is intentionally large enough for real office files, including highly compressed legacy
corpus documents, but prevents an input from growing without bound through ZIP expansion,
markup nesting, external images or embedded objects.

```csharp
using Quillwright.Diagnostics;
using Quillwright.Model;

DocumentLoadBudget budget = DocumentLoadBudget.Default with
{
    MaxInputBytes = 32 * 1024 * 1024,
    MaxInflatedBytes = 128 * 1024 * 1024,
    MaxPartBytes = 32 * 1024 * 1024,
    MaxPackageParts = 4_096,
};

WordDocument document = await WordDocument.LoadAsync(
    "upload.docx",
    new LoadOptions { Budget = budget });
```

Limits are inclusive: a source exactly as large as its configured ceiling is accepted. DOCX,
legacy DOC, HTML and Markdown breaches throw `DocumentLoadLimitException`; its `LimitName`,
`Limit` and `Observed` properties are stable machine-readable values. Malformed-file exceptions
and recoverable `LoadDiagnostics` remain separate, so a service can distinguish a rejected
resource budget from damaged markup.

RTF uses the same budget values but preserves its established exception contract: input-size,
group-depth and decoded-text breaches raise `RtfFormatException`. Those exceptions do not expose
the machine-readable `DocumentLoadLimitException` properties. This lets existing RTF callers keep
one malformed-or-refused-input catch path while sharing policy with the other importers.

## What is bounded

| Budget property | Work it limits |
| --- | --- |
| `MaxInputBytes` | Compressed DOCX/RTF bytes, the complete CFB `.doc`, and encoded HTML/Markdown files |
| `MaxPackageParts` | OPC ZIP entries and CFB directory entries |
| `MaxInflatedBytes`, `MaxPartBytes` | Total and per-part OPC expansion; declared CFB stream payloads |
| `MaxXmlCharactersPerPart`, `MaxXmlNodes`, `MaxXmlDepth` | XML parts read or preserved from an OPC package |
| `MaxTextCharacters` | Decoded HTML, Markdown and RTF source |
| `MaxLines` | HTML and Markdown source lines |
| `MaxMarkupNodes` | HTML tree construction and Markdown block/inline parsing |
| `MaxMarkupDepth` | HTML/Markdown nesting and RTF group depth |
| `MaxMediaBytes`, `MaxTotalMediaBytes` | OPC image/audio/video parts identified by content type or relationship role, local HTML/Markdown images, data URIs and inflated `.doc` metafiles |
| `MaxEmbeddedObjectBytes`, `MaxEmbeddedObjects` | OPC `oleObject`/`package` relationship targets and reconstructed `.doc` object-pool payloads |

Seekable file and stream lengths are checked before a whole-file allocation. Non-seekable
streams are copied through a bounded reader and stop on the first byte beyond the ceiling.
ZIP central-directory lengths are checked before part buffers are allocated; decompressed copy
paths retain the same per-part check. XML and markup counters are shared across one import,
not reset for every nested parser.

OPC resource limits do not depend on the conventional `/media/` and `/embeddings/` directory
names. ECMA-376 permits relationship targets at other valid part names, so relationship roles
and registered content types are classified before resource-specific ceilings are checked.
Directory names are retained only as a conservative fallback when package metadata is damaged.

## Applying one policy to every format

```csharp
DocumentLoadBudget budget = DocumentLoadBudget.Default with
{
    MaxInputBytes = 16 * 1024 * 1024,
    MaxTextCharacters = 2_000_000,
    MaxMarkupNodes = 250_000,
    MaxMarkupDepth = 96,
    MaxLines = 200_000,
    MaxMediaBytes = 8 * 1024 * 1024,
    MaxTotalMediaBytes = 32 * 1024 * 1024,
};

WordDocument docx = await WordDocument.LoadAsync("upload.docx", new LoadOptions { Budget = budget });
WordDocument doc = await DocReader.LoadWithOptionsAsync("upload.doc", new DocImportOptions { Budget = budget });
HtmlImportResult html = HtmlImporter.Import(sourceHtml, new HtmlImportOptions { Budget = budget });
MarkdownImportResult markdown = MarkdownImporter.Import(sourceMarkdown, new MarkdownImportOptions { Budget = budget });
RtfImportResult rtf = RtfReader.Load(sourceRtf, new RtfImportOptions { Budget = budget });
```

`DocxReader.OpenWithOptionsAsync(path, loadOptions)` uses the same OPC limits while yielding blocks.
`RtfImportOptions.MaxInputBytes`, `MaxGroupDepth` and `MaxTextCharacters` remain source-compatible
aliases that update the corresponding properties of `Budget`; breaches of those aliases retain
the `RtfFormatException` behavior described above.

Raising a limit is an explicit trust decision. A larger ceiling permits a larger allocation;
it does not turn preservation-first DOCX loading into a streaming operation. For text extraction
where the full model is unnecessary, prefer `DocxReader` and still pass a budget.
