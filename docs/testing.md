# Running the tests

```powershell
dotnet test Quillwright.slnx
```

A Windows clone runs the complete suite green out of the box. Some tests skip, and that is by
design: they read a corpus of real Word documents that belongs to other projects and is not part
of this repository. The skip message says which collection is missing and where to get it, so a
run that skips is not a run that is broken.

## Linux font boundary

PDF line breaks, page counts, link destinations, and glyph positions are part of the tested
contract, so changing the selected font is not harmless test noise. Ubuntu CI first installs the
same minimal font inventory used by the package smoke and then runs the Linux-compatible suite:

```bash
bash ./eng/install-linux-fonts.sh
dotnet test Quillwright.slnx --filter "RequiresMicrosoftFonts!=true"
```

`RequiresMicrosoftFonts` is reserved for PDF assertions that empirically depend on a Microsoft
font's exact metrics or glyph repertoire. It is not a generic Linux quarantine: tests that pass
with the fixed Carlito, Caladea, and DejaVu inventory remain enabled on both platforms. The full
unfiltered suite remains required on Windows, and a filtered Linux pass must never be reported as
equivalent to it. The rationale for the deliberately small package set is recorded in
[`docs/releasing.md`](releasing.md).

## Coverage ratchet

```powershell
pwsh ./eng/coverage.ps1
```

The coverage gate runs on Windows because it executes the complete, unfiltered PDF suite. It
builds once, runs the five test projects sequentially, and merges their Coverlet JSON into one
result before applying line and branch ratchets separately to every runtime package. Sequential
merging keeps a later project from overwriting evidence produced by an earlier one. The thresholds
are calibrated for a clean clone without either optional corpus, so installing the corpora can
only add evidence; it is never required to satisfy the ratchet. The raw merged result is written
to `artifacts/coverage/coverage.json`. Use `-NoRestore` and `-NoBuild` only when those steps have
already completed for the same Release artifacts.

## What is checked without any corpus

Everything the library can demonstrate on documents it writes itself: the model, the reader and
the writer against each other, the style resolver, fields, equations, templating, tracked
changes, comparison, Markdown and HTML in both directions, the HTML parser against the WHATWG
algorithm, signatures, encryption, `.doc` written and read back, and PDF rendered from the
`.docm` fixtures in `tests/fixtures/`, which are built by
[`tests/fixtures/build-fixtures.ps1`](../tests/fixtures/build-fixtures.ps1) and committed.

## What needs the corpus

The claims that only real files can support: how packages produced by Word over two decades
behave through a load/save/reload, which copy-through bytes and relationships survive, whether
validator-clean inputs remain clean, whether legacy `.doc` inputs convert to packages the Open
XML SDK validator accepts, whether every conversion warning fires on a file this library did
not write, and what the OfficeArt drawing layer actually holds in practice.

Two collections, neither of them ours:

| Directory | What it is | Where it comes from |
| --- | --- | --- |
| `Open-XML-SDK-<version>` | The test assets of the Open XML SDK, under `test/DocumentFormat.OpenXml.Tests.Assets` | <https://github.com/dotnet/Open-XML-SDK> |
| `DocumentsTelerik` | The test documents of Telerik Document Processing | <https://github.com/telerik/document-processing-sdk> |

Unpack them **beside the repository** and the tests find them:

```
somewhere/
├── quillwright/          ← this repository
├── Open-XML-SDK-3.5.1/
└── DocumentsTelerik/
```

Anywhere else works too, through environment variables:

```bash
# One directory holding both, under the names above
export QUILLWRIGHT_CORPUS=/data/word-corpora

# Or each one on its own
export QUILLWRIGHT_CORPUS_OPENXML=/data/Open-XML-SDK-3.5.1
export QUILLWRIGHT_CORPUS_TELERIK=/data/DocumentsTelerik
```

The resolution order is: the two specific variables, then `QUILLWRIGHT_CORPUS`, then the
directory the repository sits in. It lives in one place,
[`tests/ReferenceCorpus.cs`](../tests/ReferenceCorpus.cs), shared by every test project.

## What the 934-file DOCX oracle proves

The documented snapshot contains 419 `.docx` files from the Open XML SDK 3.5.1 assets and 515
from Telerik, after excluding empty files and files of 8 MiB or more. The result is deliberately
split into countable outcomes rather than treating an exception as a successful round trip:

- 932 unencrypted packages must load, save and reload. Any new `DocxFormatException` fails the
  round-trip theory.
- Two Open XML SDK fixtures named `document with password.docx` are listed by collection,
  relative path and exact message in an expected-refusal manifest. Each must throw exactly
  `EncryptedDocumentException`; a missing, renamed or newly loadable fixture fails its own
  theory.

Two of the 932 loadable files are deliberately malformed SDK assets for which the SDK
validator throws instead of returning validation errors. `InvalidDocProps.docx` has a dangling
relationship to `/docProps/core.xml`; `InvalidDocPropsct.docx` gives that part an invalid
content type. Their paths, exact exception types and identifying message fragments form a
second manifest and two additional test cases. They still have to pass every preservation and
reload assertion below, but they contribute no evidence to the validation claim.

For each of the 932 loadable packages the oracle requires all of the following:

- `WordDocument.GetText()` is identical after save and reload.
- Every original non-relationship OPC part is still present. ZIP directory entries are not OPC
  parts and are intentionally ignored.
- Every part classified by the save plan as copy-through retains the same SHA-256 and effective
  content type. Model-owned XML is regenerated and is therefore not required to be
  byte-identical.
- Every original relationship survives with the same source part, id, type, target and target
  mode. Relationship XML ordering and an empty `.rels` file are not treated as semantics, and
  new relationships needed by regenerated model parts are allowed.
- Except for the two explicit SDK-validator rejections above, if the original package validates
  cleanly against the Open XML SDK's Office 2019 schema, the saved package must also validate
  cleanly.

That last condition is intentionally one-way. Other upstream fixtures yield pre-existing schema
errors normally, and the test does not claim to count whether a save added a different error to
an already-invalid package. It also does not prove pixel-identical Word layout or byte identity
for the document, styles, notes, comments, headers and other model-owned XML. Those are separate
semantic and Word-oracle concerns.

## Why the skips are visible

A test that needs data it does not have has three ways to behave, and two of them are wrong.

Failing is the worst: whoever clones the repository sees red and concludes the library is
broken. Disappearing is subtler and nearly as bad — an xUnit theory whose `MemberData` yields
nothing does not fail and does not skip, it simply is not there, and the total drops without
saying why. The corpus theories therefore emit one empty case and skip on it in the body. The
round-trip, expected-refusal and validator-rejection theories each remain visible when their
collection is absent, rather than silently disappearing from discovery.

Compare the totals, not just the failures, when a change to the corpus wiring is in the diff.

## Release gates against real files

`.github/workflows/corpus.yml` runs every test project against the Open XML SDK test assets, pinned
by commit and hash in `eng/corpus/manifest.json`. It runs weekly and on demand, not on pull requests,
so nobody has to download someone else's documents to contribute. `verify_results.py` fails the job
if a test skips that the manifest does not allow, or if fewer tests ran than the manifest expects.
The Telerik collection has no public pinned source, so tests that need it are on the allowed list.

On Windows the fetcher uses extended filesystem paths: the upstream generated sources exceed
`MAX_PATH` when staged under a normal checkout. No machine-wide long-path setting is needed.
The temporary extraction directory stays under the requested corpus root; a pre-existing corpus
destination is refused so that stale files cannot mix with the pinned archive.

Every corpus round trip also goes through `PackageInvariants`: unique zip entries, relationship
targets that exist, content types for every part. Only problems the save introduced count, because
the corpus contains files that were already invalid when they were written.
