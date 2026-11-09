# Quillwright benchmarks

Latest measured run: **2026-11-09**, **Quillwright 0.2.5**.

- [Results, environment, limitations and reproduction commands](../docs/benchmarks.md)
- [Machine-readable snapshot and source/assembly hashes](../docs/benchmark-results/2026-11-09.json)
- [Raw BenchmarkDotNet reports](../docs/benchmark-results/2026-11-09/)
- [Website charts](https://wrightsuite.com/#benchmarks)

The measured source is [`afc415dd3947`](https://github.com/WrightDocumentSuite/quillwright/tree/afc415dd3947fce79dc79663a84cd93526d528f2).
The benchmark project is [Quillwright.Benchmarks](Quillwright.Benchmarks/).

The run used a shared Windows Server 2025 host with an Intel Xeon Gold 5412U,
.NET SDK 10.0.403, runtime 10.0.14 and BenchmarkDotNet 0.15.8. Timing tables
include confidence margins; allocations use binary KiB/MiB per operation.
Results from the August i7-8700 run are a different hardware baseline, not
evidence of a code speedup. Follow the report's pinned source and per-class
commands when reproducing; a normal build alone does not refresh measurements.
