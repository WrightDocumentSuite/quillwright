using BenchmarkDotNet.Attributes;
using DocumentFormat.OpenXml.Packaging;
using Quillwright.Model;
using Quillwright.Streaming;

namespace Quillwright.Benchmarks;

/// <summary>
/// Reading a document back: the whole model, and text extraction alone.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ReadingBenchmarks
{
    private byte[] _package = [];

    [Params(20_000)]
    public int Paragraphs { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        for (int i = 0; i < Paragraphs; i++)
            section.AddParagraph($"Line {i} of the generated report.");

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer);
        _package = buffer.ToArray();
    }

    [Benchmark(Baseline = true, Description = "Quillwright model")]
    public async Task<int> QuillwrightModel()
    {
        WordDocument document = await WordDocument.LoadAsync(new MemoryStream(_package));
        return document.Paragraphs.Count();
    }

    [Benchmark(Description = "Quillwright streaming text")]
    public async Task<int> QuillwrightStreaming()
    {
        var count = 0;
        DocxReader reader = await DocxReader.OpenAsync(new MemoryStream(_package));
        await using (reader)
        {
            await foreach (string _ in reader.ReadTextAsync())
                count++;
        }

        return count;
    }

    [Benchmark(Description = "Open XML SDK")]
    public int OpenXmlSdk()
    {
        using WordprocessingDocument document = WordprocessingDocument.Open(new MemoryStream(_package), false);
        return document.MainDocumentPart?.Document?.Body?
            .Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Count() ?? 0;
    }
}
