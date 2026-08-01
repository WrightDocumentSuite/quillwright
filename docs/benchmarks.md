# Benchmarks

```bash
dotnet run -c Release --project benchmarks/Quillwright.Benchmarks -- --filter *
```

BenchmarkDotNet 0.15.8, .NET 10.0.10, Windows Server 2022, Intel Core i7-8700, 3 warmup and
10 measured iterations per method.

The comparison is against the Open XML SDK 3.5, which is the reference implementation and the
thing most .NET code uses today. NPOI would be the other natural comparison, but every
published version of the `System.Security.Cryptography.Xml` package it depends on carries
security advisories, and a library repository should not ship one.

## Generating 20 000 paragraphs

Every tenth paragraph is bold, so the writer cannot collapse the run properties away.

| | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| **Quillwright streaming** | **17.0 ms** | **1.00** | **17.8 MB** |
| Quillwright model | 32.7 ms | 1.93 | 18.3 MB |
| Open XML SDK 3.5 | 46.8 ms | 2.76 | 18.7 MB |

## Reading 20 000 paragraphs

| | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| Quillwright model | 27.4 ms | 1.00 | 18.3 MB |
| **Quillwright streaming text** | **16.6 ms** | **0.61** | 16.8 MB |
| Open XML SDK 3.5 | 58.1 ms | 2.13 | 15.8 MB |

## The legacy binary format, 20 000 paragraphs

There is nothing to compare against here: the Open XML SDK does not read `.doc` at all, and
the libraries that do are unmaintained. These numbers say what the older format costs against
the newer one rather than how the library ranks.

| | Mean | Allocated |
| --- | ---: | ---: |
| Write `.doc` | 56.2 ms | 48.3 MB |
| Read `.doc` | 58.2 ms | 49.6 MB |

Both directions are around twice the time and nearly three times the allocation of the same
document as `.docx`, and the reason is structural rather than sloppy. A `.docx` is written in
one forward pass; a `.doc` cannot be, because the header records where the text ended, the
formatting pages record byte offsets into that text, and the piece table ties the two
together, so the whole file is built in memory and stitched afterwards. Reading pays the same
cost in reverse: the text has no structure of its own, and the paragraph boundaries come from
formatting pages that have to be indexed before the first paragraph can be produced.

## Reading it honestly

Quillwright is roughly two to three times faster than the SDK on both paths, and allocates
about the same. It is not an order of magnitude, and the allocation figures are not better —
the SDK allocates slightly less when reading, because it builds a lazier tree than a full
document model does.

Where the design pays off is in the shape of the work rather than these totals. The streaming
writer produces the same markup as the model for half the time, and the streaming reader beats
the model it shares its parsing code with. The paragraph representation is what makes search
and replace across run boundaries free rather than a separate stitching pass, and that cost
does not appear in a benchmark that only reads or only writes.

The reading numbers also record a mistake worth keeping. The first streaming reader buffered
each block into a string and stood up an `XmlReader` for it — convenient, and reusing the
document reader wholesale. It allocated 330 MB, eighteen times what loading the entire
document did. Parsing straight off the decompressed stream fixed it. "Streaming" is a claim
about memory, and it has to be measured like one.

## Corpus

The correctness suite is the more interesting measurement. Every run loads 934 real `.docx`
files and 249 real `.doc` files from the Open XML SDK and Telerik reference repositories,
saves each one, and asserts that no package part was lost, that the text is identical after a
reload, and that a document which validated before still validates after. That whole suite
runs in about forty seconds.
