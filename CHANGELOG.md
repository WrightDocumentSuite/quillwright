# Changelog

Notable changes to Quillwright are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) within the limits of the pre-1.0 API.

## [Unreleased]

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

[Unreleased]: https://github.com/WrightDocumentSuite/quillwright/compare/v0.2.5...HEAD
[0.2.5]: https://github.com/WrightDocumentSuite/quillwright/compare/v0.2.4...v0.2.5
[0.2.4]: https://github.com/WrightDocumentSuite/quillwright/compare/v0.2.3...v0.2.4
[0.2.3]: https://github.com/WrightDocumentSuite/quillwright/compare/v0.2.2...v0.2.3
[0.2.2]: https://github.com/WrightDocumentSuite/quillwright/releases/tag/v0.2.2
