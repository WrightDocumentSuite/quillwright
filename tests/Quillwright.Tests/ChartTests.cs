using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Charts, read from the numbers a chart part caches rather than from the workbook they came
/// out of. That cache is what the document actually draws.
/// </summary>
public class ChartTests
{
    private static readonly string[] CorpusRoots = ReferenceCorpus.Roots;

    /// <summary>A Word file whose three charts are the sample data Word inserts by default.</summary>
    private const string Fixture = "2D Column-O12-Word-Charts.docx";

    [Fact]
    public async Task AChart_ReadsItsKindAndItsSeries()
    {
        WordDocument document = await FixtureAsync();

        Chart chart = document.Charts.First(static c => c.Series.Count > 0);

        Assert.Equal(ChartKind.Bar, chart.Kind);
        Assert.Equal(3, chart.Series.Count);
        Assert.Equal("Series 1", chart.Series[0].Name);
        Assert.Equal(["Category 1", "Category 2", "Category 3", "Category 4"], chart.Series[0].Categories);
        Assert.Equal([4.3, 2.5, 3.5, 4.5], chart.Series[0].Values);
    }

    [Fact]
    public async Task EveryChartPart_IsFound()
    {
        WordDocument document = await FixtureAsync();

        Assert.NotEmpty(document.Charts);
        Assert.All(document.Charts, static chart =>
            Assert.Contains("chart", chart.Location, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Charts have no API for writing, so the parts have to come through untouched.</summary>
    [Fact]
    public async Task Charts_SurviveTheRoundTrip()
    {
        WordDocument document = await FixtureAsync();
        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        WordDocument reloaded = await WordDocument.LoadAsync(saved, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(document.Charts.Count, reloaded.Charts.Count);
        Assert.Equal(
            document.Charts.SelectMany(static c => c.Series).SelectMany(static s => s.Values),
            reloaded.Charts.SelectMany(static c => c.Series).SelectMany(static s => s.Values));
    }

    /// <summary>
    /// The corpus is what proves the reader copes with charts of every kind Word writes, and
    /// with the Strict spelling of the chart namespace that some producers use.
    /// </summary>
    [Fact]
    public async Task ChartsOfSeveralKinds_AreFoundInTheCorpus()
    {
        List<Chart> charts = [];
        foreach (string path in Corpus())
        {
            try
            {
                charts.AddRange((await WordDocument.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken)).Charts);
            }
            catch (Diagnostics.DocxFormatException)
            {
                // A corpus of test files includes deliberately corrupt ones.
            }
        }

        Assert.SkipWhen(charts.Count == 0, ReferenceCorpus.Absent);
        Assert.True(charts.Count(static c => c.Kind != ChartKind.Unknown) >= 10, $"only {charts.Count} charts were read");
        Assert.True(charts.Select(static c => c.Kind).Distinct().Count() > 1, "every chart read as the same kind");
        Assert.Contains(charts, static c => c.Series.Any(static s => s.Values.Count > 0));
    }

    /// <summary>
    /// The one write charts have: the data is replaced, the file saves, and what the saved
    /// file draws is the new numbers.
    /// </summary>
    [Fact]
    public async Task NewData_SurvivesTheRoundTrip()
    {
        WordDocument document = await FixtureAsync();
        Chart chart = document.Charts.First(static c => c.Series.Count == 3);

        ChartSeries[] replacement =
        [
            new ChartSeries { Name = "North", Categories = ["Q1", "Q2"], Values = [10, 20] },
            new ChartSeries { Name = "South", Categories = ["Q1", "Q2"], Values = [30, null] },
            new ChartSeries { Categories = ["Q1", "Q2"], Values = [50, 60] },
        ];

        Chart updated = document.SetChartData(chart, replacement);
        Assert.Equal("North", updated.Series[0].Name);

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        WordDocument reloaded = await WordDocument.LoadAsync(saved, cancellationToken: TestContext.Current.CancellationToken);

        Chart persisted = reloaded.Charts.First(c => c.Location == chart.Location);
        Assert.Equal("North", persisted.Series[0].Name);
        Assert.Equal(["Q1", "Q2"], persisted.Series[0].Categories);
        Assert.Equal([10.0, 20.0], persisted.Series[0].Values);
        Assert.Equal([30.0], persisted.Series[1].Values);

        // The third series kept the name the file already had for it.
        Assert.Equal(chart.Series[2].Name, persisted.Series[2].Name);
    }

    private static async Task<WordDocument> FixtureAsync()
    {
        string? path = Corpus().FirstOrDefault(static p => Path.GetFileName(p) == Fixture);
        Assert.SkipWhen(path is null, ReferenceCorpus.Absent);
        return await WordDocument.LoadAsync(path!, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static IEnumerable<string> Corpus()
    {
        foreach (string root in CorpusRoots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (string path in Directory.EnumerateFiles(root, "*.docx", SearchOption.AllDirectories))
            {
                if (new FileInfo(path).Length is > 0 and < 8 * 1024 * 1024)
                    yield return path;
            }
        }
    }
}
