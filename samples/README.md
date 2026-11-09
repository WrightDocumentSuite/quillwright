# Quillwright runnable examples

Run from the repository root with the SDK pinned in `global.json`:

```sh
dotnet run -c Release --project samples/Quillwright.Samples -- ./sample-output
```

The optional first argument is the output directory; use an empty directory to keep results separate from your files.

The existing examples run first, followed by these self-contained scenarios. Each new scenario creates its inputs, reloads or checks its output, and throws if the demonstrated result changes. All captions and messages use English.

| Example | Scenario | Output |
| --- | --- | --- |
| [MarkdownRoundTrip](Quillwright.Samples/MarkdownRoundTrip.cs) | Import a release plan, save DOCX, reload and export Markdown with diagnostics. | `06-markdown.docx; markdown-roundtrip/document.md` |
| [HtmlPreview](Quillwright.Samples/HtmlPreview.cs) | Import headings, bold text and lists, then save a self-contained HTML preview. HTML preview does not paginate the document. | `07-html-import.docx; html-preview/document.html` |
| [CompareRevisions](Quillwright.Samples/CompareRevisions.cs) | Compare two versions, save tracked revisions, then verify both accepted and rejected text. | `08-redline.docx; 08-accepted.docx; 08-rejected.docx` |

Each source file exposes `RunAsync(directory)` and can also be called independently. See the [getting-started guide](../docs/getting-started.md) for package installation. The same new method bodies are shown in the [website playground](https://wrightsuite.com/#code-playground).

Markdown and HTML preserve their documented semantic subset, not every layout feature. Inspect import/export diagnostics. HTML is a preview, while Word controls pagination in DOCX. Revision acceptance and rejection are demonstrated on separate reloads so the redline remains available.
