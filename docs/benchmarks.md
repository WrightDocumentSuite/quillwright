# Benchmarks

Measured on **2026-11-09**, Quillwright **0.2.5**, [public source `afc415dd3947`](https://github.com/WrightDocumentSuite/quillwright/tree/afc415dd3947fce79dc79663a84cd93526d528f2).

Environment: Microsoft Windows Server 2025 Datacenter (build 26100), Intel Xeon Gold 5412U, 24 cores / 48 logical processors, 255.7 GiB RAM; .NET SDK 10.0.403, .NET 10.0.14 (10.0.14, 10.0.1426.52326), BenchmarkDotNet 0.15.8.

The server also ran other workloads. Suites and benchmark classes ran sequentially with their committed job parameters. These are shared-host measurements; the August i7-8700 results are a different hardware baseline and do not establish a code speedup.

Tables use the first complete pass throughout. Timings and allocations are per operation; KiB and MiB are binary units. Error is BenchmarkDotNet's reported confidence margin, N is its retained sample count, and CV is standard deviation divided by mean. High-CV rows and confidence margins larger than the mean should be treated as noisy observations.

The [machine-readable snapshot](benchmark-results/2026-11-09.json) records exact assembly versions, hashes, source commit, and every measured row. Runtime servicing and competitor versions are part of this snapshot, rather than labels copied from the previous tables.

| Assembly | Product version |
| --- | --- |
| `BenchmarkDotNet.Annotations.dll` | `0.15.8+1e8238ef6c08e18821edae83ac8d6b0a2bb1bb98` |
| `BenchmarkDotNet.dll` | `0.15.8+1e8238ef6c08e18821edae83ac8d6b0a2bb1bb98` |
| `DocumentFormat.OpenXml.dll` | `3.5.1+Branch.main.Sha.3139fdfd27414548a41555f7848d5728f6e71a42.3139fdfd27414548a41555f7848d5728f6e71a42` |
| `DocumentFormat.OpenXml.Framework.dll` | `3.5.1+Branch.main.Sha.3139fdfd27414548a41555f7848d5728f6e71a42.3139fdfd27414548a41555f7848d5728f6e71a42` |
| `Inkwright.dll` | `0.1.4+5028efe3b640193b92863f7b94f3452809d88107` |
| `Quillwright.Benchmarks.dll` | `0.2.5+afc415dd3947fce79dc79663a84cd93526d528f2` |
| `Quillwright.dll` | `0.2.5+afc415dd3947fce79dc79663a84cd93526d528f2` |
| `Quillwright.Doc.dll` | `0.2.5+afc415dd3947fce79dc79663a84cd93526d528f2` |
| `Quillwright.Pdf.dll` | `0.2.5+afc415dd3947fce79dc79663a84cd93526d528f2` |

## GenerationBenchmarks

| Method | Parameters | Mean | Error | Median | Allocated | N | CV |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| QuillwrightStreaming | Paragraphs=20000 | 12.350 ms | 1.151 ms | 12.065 ms | 17.824 MiB | 9 | 5.5% |
| QuillwrightModel | Paragraphs=20000 | 19.537 ms | 1.999 ms | 19.647 ms | 18.328 MiB | 10 | 6.8% |
| OpenXmlSdk | Paragraphs=20000 | 27.021 ms | 1.279 ms | 27.219 ms | 18.721 MiB | 9 | 2.8% |

## LegacyBenchmarks

| Method | Parameters | Mean | Error | Median | Allocated | N | CV |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Write | Paragraphs=20000 | 45.687 ms | 669.711 µs | 45.764 ms | 48.278 MiB | 10 | 1.0% |
| Read | Paragraphs=20000 | 31.567 ms | 2.115 ms | 31.163 ms | 51.298 MiB | 10 | 4.4% |

## PdfBenchmarks

| Method | Parameters | Mean | Error | Median | Allocated | N | CV |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Prose | Paragraphs=2000 | 84.894 ms | 3.262 ms | 83.617 ms | 108.353 MiB | 10 | 2.5% |
| Tagged | Paragraphs=2000 | 97.933 ms | 6.672 ms | 96.097 ms | 117.539 MiB | 10 | 4.5% |
| Tabular | Paragraphs=2000 | 48.050 ms | 7.498 ms | 44.867 ms | 59.626 MiB | 10 | 10.3% |

## ReadingBenchmarks

| Method | Parameters | Mean | Error | Median | Allocated | N | CV |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| QuillwrightModel | Paragraphs=20000 | 21.108 ms | 1.111 ms | 20.969 ms | 20.343 MiB | 9 | 3.1% |
| QuillwrightStreaming | Paragraphs=20000 | 19.335 ms | 995.602 µs | 19.263 ms | 18.839 MiB | 10 | 3.4% |
| OpenXmlSdk | Paragraphs=20000 | 41.831 ms | 473.714 µs | 41.856 ms | 15.772 MiB | 10 | 0.7% |

## Reproduce

Use a clean standalone public checkout at the source commit above, the pinned SDK, and the required fonts. Run each class in a separate process with a distinct artifact directory; this avoids shared generated-build cleanup between benchmark sessions.

```sh
dotnet restore benchmarks/Quillwright.Benchmarks/Quillwright.Benchmarks.csproj --locked-mode
dotnet build benchmarks/Quillwright.Benchmarks/Quillwright.Benchmarks.csproj -c Release --no-restore
dotnet run -c Release --project benchmarks/Quillwright.Benchmarks/Quillwright.Benchmarks.csproj --no-build -- --filter "Quillwright.Benchmarks.GenerationBenchmarks.*" --exporters json --artifacts "BenchmarkDotNet.Artifacts/GenerationBenchmarks"
dotnet run -c Release --project benchmarks/Quillwright.Benchmarks/Quillwright.Benchmarks.csproj --no-build -- --filter "Quillwright.Benchmarks.LegacyBenchmarks.*" --exporters json --artifacts "BenchmarkDotNet.Artifacts/LegacyBenchmarks"
dotnet run -c Release --project benchmarks/Quillwright.Benchmarks/Quillwright.Benchmarks.csproj --no-build -- --filter "Quillwright.Benchmarks.PdfBenchmarks.*" --exporters json --artifacts "BenchmarkDotNet.Artifacts/PdfBenchmarks"
dotnet run -c Release --project benchmarks/Quillwright.Benchmarks/Quillwright.Benchmarks.csproj --no-build -- --filter "Quillwright.Benchmarks.ReadingBenchmarks.*" --exporters json --artifacts "BenchmarkDotNet.Artifacts/ReadingBenchmarks"
```

For a new comparison, rerun the complete suite and retain the environment and raw reports.

## Historical results and correctness

The [previous benchmark document](https://github.com/WrightDocumentSuite/quillwright/blob/afc415dd3947fce79dc79663a84cd93526d528f2/docs/benchmarks.md) preserves the August measurements and their correctness/corpus notes. Corpus snapshots and build/test wall-clock telemetry are separate evidence from the measurements above.
