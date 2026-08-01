# Running the tests

```bash
dotnet test Quillwright.slnx
```

A clone runs green out of the box. Some tests skip, and that is by design: they read a corpus
of real Word documents that belongs to other projects and is not part of this repository. The
skip message says which collection is missing and where to get it, so a run that skips is not
a run that is broken.

## What is checked without any corpus

Everything the library can demonstrate on documents it writes itself: the model, the reader and
the writer against each other, the style resolver, fields, equations, templating, tracked
changes, comparison, Markdown and HTML in both directions, the HTML parser against the WHATWG
algorithm, signatures, encryption, `.doc` written and read back, and PDF rendered from the
`.docm` fixtures in `tests/fixtures/`, which are built by
[`tests/fixtures/build-fixtures.ps1`](../tests/fixtures/build-fixtures.ps1) and committed.

## What needs the corpus

The claims that only real files can support: that 934 documents produced by Word over two
decades round-trip with no part lost, that 249 legacy `.doc` files convert to packages the Open
XML SDK validator accepts, that every conversion warning fires on a file this library did not
write, and what the OfficeArt drawing layer actually holds in practice.

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

## Why the skips are visible

A test that needs data it does not have has three ways to behave, and two of them are wrong.

Failing is the worst: whoever clones the repository sees red and concludes the library is
broken. Disappearing is subtler and nearly as bad — an xUnit theory whose `MemberData` yields
nothing does not fail and does not skip, it simply is not there, and the total drops without
saying why. The corpus theories therefore emit one empty case and skip on it in the body, so a
run without the corpus reports the same number of tests as a run with it, with the difference
in the skip count rather than in the total.

Compare the totals, not just the failures, when a change to the corpus wiring is in the diff.
