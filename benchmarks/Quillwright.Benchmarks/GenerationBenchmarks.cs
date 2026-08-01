using BenchmarkDotNet.Attributes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Quillwright.Model;
using Quillwright.Streaming;
using Quillwright.Styles;

namespace Quillwright.Benchmarks;

/// <summary>
/// Generating a report: the case where a document is written once and never read back.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class GenerationBenchmarks
{
    [Params(20_000)]
    public int Paragraphs { get; set; }

    [Benchmark(Baseline = true, Description = "Quillwright streaming")]
    public async Task<long> QuillwrightStreaming()
    {
        var buffer = new MemoryStream();
        DocxWriter writer = await DocxWriter.CreateAsync(buffer);
        await using (writer)
        {
            for (int i = 0; i < Paragraphs; i++)
            {
                writer.WriteParagraph($"Line {i} of the generated report.",
                    RunFormat.Default with { Bold = i % 10 == 0 });
                await writer.FlushIfNeededAsync();
            }
        }

        return buffer.Length;
    }

    [Benchmark(Description = "Quillwright model")]
    public async Task<long> QuillwrightModel()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        for (int i = 0; i < Paragraphs; i++)
        {
            Quillwright.Model.Paragraph paragraph = section.AddParagraph();
            paragraph.AppendText($"Line {i} of the generated report.", RunFormat.Default with { Bold = i % 10 == 0 });
        }

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer);
        return buffer.Length;
    }

    [Benchmark(Description = "Open XML SDK")]
    public long OpenXmlSdk()
    {
        var buffer = new MemoryStream();
        using (WordprocessingDocument document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = document.AddMainDocumentPart();
            var body = new Body();
            for (int i = 0; i < Paragraphs; i++)
            {
                var run = new DocumentFormat.OpenXml.Wordprocessing.Run(new Text($"Line {i} of the generated report."));
                if (i % 10 == 0)
                    run.RunProperties = new RunProperties(new Bold());
                body.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(run));
            }

            main.Document = new Document(body);
        }

        return buffer.Length;
    }
}
