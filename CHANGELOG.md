# Changelog

Notable changes to Quillwright are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) within the limits of the pre-1.0 API.

## [Unreleased]

## [0.2.6] - 2026-11-09

### Added

- `PdfExportOptions.RefuseWhen` stops a render with `PdfExportRefusedException` on the compromises
  it selects, using the existing diagnostics. `PdfExportRefusal` has ready-made policies.
- `SaveSanitizedAsync` writes a copy without the chosen `ContentCategories` (macros, embedded
  objects, external links, custom XML, signatures) and reports what was removed and what was kept.

### Fixed

- Template generator output names use bounded hashes of complete type identities, so long or
  nested model names do not exceed filesystem file-name limits.
- Saving a document again after adding an image, header or footer no longer reuses a part name that
  is already taken. The later part used to replace the earlier one, and two pictures or sections
  showed the same content. Writing a package part twice now raises `InvalidOperationException`.
- Tracked formatting changes count as revisions and are resolved by `AcceptAllRevisions` and
  `RejectAllRevisions`, which restore the previous formatting on rejection. `ApplyFormat(Func)`
  records them like `ApplyFormat(start, length, ...)`, and a run changed twice rejects to its
  original look.
- Tracked paragraph-property changes are resolved the same way. Tracked table, row and section
  property changes are now detected and can be accepted; rejecting them is not supported and
  leaves them in place, reported by `HasRevisions`.
- Accepting a deleted paragraph mark keeps the next paragraph's runs, pictures, links and bookmarks.
  The merged paragraph takes the formatting of the mark that survives.
- Headers and footers are identified by their relationship type, so a footer in a part with any
  name loads as a footer. A root element that disagrees with the relationship raises a warning.
- Removing the last custom property no longer leaves a root relationship pointing at a missing
  part. An application properties part the model cannot read is copied through unchanged instead
  of being lost.

### Deprecated

- `LoadOptions.PreserveUnknownContent` never had any effect and is now marked obsolete. Package
  parts the model does not represent are always preserved, so it cannot remove macros, embedded
  objects or custom XML.

### Changed

- PDF export uses Inkwright 0.1.5 through exact package ranges.

## [0.2.5] - 2026-11-08

### Added

- Cross-platform CI and an opt-in, verified NuGet release workflow.
- A repeatable coverage gate, package-consumer smoke tests, and contributor/security guidance.
- Explicit load budgets and cancellation for standalone Office add-in manifest parsing.
- RTF 1.9.1 stylesheet (`\stylesheet`), hyperlinks (`HYPERLINK`), bookmarks (`\bkmkstart`/`\bkmkend`),
  table and cell borders (`\trbrdr*`/`\clbrdr*`), multi-paragraph/nested table cell blocks, and
  inline picture `DocumentLoadBudget` enforcement (`MaxMediaParts`, `MaxPartBytes`, `MaxTotalPartBytes`).
- PDF chart rendering for `Radar` (polygonal spider-web grid and radial axes), `Scatter` point
  markers, and `Area` polygon fills in `Quillwright.Pdf`.
- NuGet lock-file support (`RestorePackagesWithLockFile`) and a `Quillwright.Fuzz` SharpFuzz
  harness for DOCX, DOC, RTF, HTML, and Markdown parsers.

### Changed

- PDF export now consumes the matching Inkwright 0.1.4 release through exact package ranges.
- Pin SDK 10.0.403 exactly and refresh SDK-owned linker dependencies in the committed lock files;
  servicing SDK selection must not silently change a locked restore.
- Real-document corpus checks now distinguish supported round trips, expected refusals, schema
  validation, and preservation evidence instead of treating every parser refusal as success.
- Office add-in `VersionOverrides` remains standalone equivalent XML, but insignificant lexical
  details can be normalized by the new bounded iterative serializer.
- Repository formatting, line-ending, package-validation, and release checks are enforced by the
  same commands locally and in CI.

### Fixed

- Linux chart tests check the expected Calibri-to-Carlito substitution, and Arabic shaping tests
  select a face with the required glyphs even when Liberation Sans is preinstalled on the runner.

- Cancellation regressions no longer depend on millisecond timers that can be starved by parallel
  test execution.
- NUL separators in `DocumentComparer` are expressed as C# escapes, so Git and Source Link treat
  the source as text without changing its runtime signatures.
- HTML import: the space that survives whitespace collapsing keeps the formatting of the text it
  came from (`a <u>word</u>` no longer underlines the space), a space before a link stays outside
  the hyperlink, and a space before an image or a note reference is no longer dropped.
- RTF and DOC round trips keep a picture's name and alternative text: RTF reads and writes the
  `wzName`/`wzDescription` shape properties of `{\*\picprop}`, and DOC writes them into the
  picture's shape and reads them from inline and floating pictures. The DOC writer no longer
  shares one data-stream record between equal images of different size or alternative text.
- `Quillwright.Pdf` declares `System.Security.Cryptography.Pkcs` 10.0.13 as a package
  dependency. Inkwright 0.1.3 accepts the vulnerable 10.0.0 (GHSA-2fmv-rv2c-59x7,
  GHSA-846f-f764-76mx), and the earlier private reference protected only this repository's build.

## [0.2.4] - 2026-08-10

### Added

- RTF parser load-budget limits and deterministic cancellation coverage.
- RTF and HTML import support for notes, with Word-backed integration coverage where available.

## [0.2.3] - 2026-08-09

### Added

- Explicit RTF 1.9.1 import and export packages with conversion diagnostics.
- Document load budgets and PDF comment rendering.

### Fixed

- The template source generator is included reliably in the published package.
- Hardened OPC resource scanning, media path resolution, digital-signature reference handling,
  comment threading, and export diagnostics.

## [0.2.2] - 2026-08-01

### Changed

- Prepared the first standalone public snapshot and NuGet packages under Apache-2.0.
- Re-measured published benchmarks and corrected package metadata and documentation.

[Unreleased]: https://github.com/WrightDocumentSuite/quillwright/compare/v0.2.6...HEAD
[0.2.6]: https://github.com/WrightDocumentSuite/quillwright/compare/v0.2.5...v0.2.6
[0.2.5]: https://github.com/WrightDocumentSuite/quillwright/compare/v0.2.4...v0.2.5
[0.2.4]: https://github.com/WrightDocumentSuite/quillwright/compare/v0.2.3...v0.2.4
[0.2.3]: https://github.com/WrightDocumentSuite/quillwright/compare/v0.2.2...v0.2.3
[0.2.2]: https://github.com/WrightDocumentSuite/quillwright/releases/tag/v0.2.2
