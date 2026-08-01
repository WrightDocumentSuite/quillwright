using BenchmarkDotNet.Attributes;
using Inkwright;
using Quillwright.Model;
using Quillwright.Pdf;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Benchmarks;

/// <summary>
/// Rendering to PDF: the cost of pagination, which is measurement rather than serialisation.
/// </summary>
/// <remarks>
/// There is nothing to compare against in the .NET world that does not shell out to Word or to a
/// browser, so these numbers say what the converter costs rather than how it ranks. What they are
/// useful for is watching the cost per page stay flat as documents grow.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class PdfBenchmarks
{
    private const string Sentence =
        "The quick brown fox jumps over the lazy dog while the cooper mends the barrel by the river. ";

    private WordDocument _prose = null!;
    private WordDocument _tabular = null!;

    [Params(2_000)]
    public int Paragraphs { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _prose = BuildProse(Paragraphs);
        _tabular = BuildTable(Paragraphs / 4);
    }

    [Benchmark(Baseline = true, Description = "Prose to PDF")]
    public int Prose() => PdfExporter.Render(_prose) is { } result ? Finish(result) : 0;

    [Benchmark(Description = "Prose to tagged PDF")]
    public int Tagged() =>
        PdfExporter.Render(_prose, new PdfExportOptions { Tagged = true }) is { } result ? Finish(result) : 0;

    [Benchmark(Description = "Table to PDF")]
    public int Tabular() => PdfExporter.Render(_tabular) is { } result ? Finish(result) : 0;

    /// <summary>Saves the result, because a render that is never written has not paid for its fonts.</summary>
    private static int Finish(PdfExportResult result)
    {
        using PdfDocument pdf = result.Document;
        return pdf.ToArray().Length;
    }

    private static WordDocument BuildProse(int paragraphs)
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Properties.DifferentFirstPage = false;
        section.Headers.GetOrCreate().AddParagraph("Benchmark report");

        Model.Paragraph footer = section.Footers.GetOrCreate().AddParagraph();
        footer.AppendText("Page ");
        footer.AppendPageNumber();

        for (int i = 0; i < paragraphs; i++)
        {
            Model.Paragraph paragraph = section.AddParagraph(Sentence + Sentence);
            paragraph.Format = paragraph.Format with { Alignment = ParagraphAlignment.Justify };
            if (i % 20 == 0)
                paragraph.Runs[0].SetFormat(RunFormat.Default with { Bold = true });
        }

        return document;
    }

    private static WordDocument BuildTable(int rows)
    {
        WordDocument document = WordDocument.Create();
        Table table = Table.Create(rows, 4, Length.FromCentimeters(16));
        table.Rows[0].Format = table.Rows[0].Format with { IsHeader = true };

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < 4; column++)
                table[row, column].SetText($"Row {row} column {column}");
        }

        document.Sections[0].Blocks.Add(table);
        return document;
    }
}
