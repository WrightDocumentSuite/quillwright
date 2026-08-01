using BenchmarkDotNet.Attributes;
using Quillwright.Doc;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Benchmarks;

/// <summary>
/// Writing and reading the Word 97-2003 binary format, which has no comparison to run
/// against: the Open XML SDK does not read it, and the alternatives that do are unmaintained.
/// The numbers are here to show the cost against the newer format rather than against a rival.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class LegacyBenchmarks
{
    private WordDocument _document = null!;
    private byte[] _file = null!;

    [Params(20_000)]
    public int Paragraphs { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = WordDocument.Create();
        Section section = _document.Sections[0];
        for (int i = 0; i < Paragraphs; i++)
        {
            Model.Paragraph paragraph = section.AddParagraph();
            paragraph.AppendText($"Line {i} of the generated report.", RunFormat.Default with { Bold = i % 10 == 0 });
        }

        _file = DocWriter.Save(_document);
    }

    [Benchmark(Baseline = true, Description = "Write .doc")]
    public long Write() => DocWriter.Save(_document).Length;

    [Benchmark(Description = "Read .doc")]
    public int Read() => DocReader.Load(_file).Sections[0].Blocks.Count;
}
