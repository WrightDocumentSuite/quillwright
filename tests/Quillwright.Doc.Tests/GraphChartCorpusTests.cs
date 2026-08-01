using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Microsoft Graph charts as Word itself wrote them.
/// </summary>
/// <remarks>
/// <para>
/// The rest of the chart tests build a <c>Workbook</c> stream by hand, which is the only way to
/// test a record layout in isolation — and a stream built by the same understanding the reader
/// has cannot find a misunderstanding. These read the charts in the reference corpus instead:
/// files Word produced, whose provenance is the Telerik test suite the corpus comes from, and
/// which nobody here had a hand in writing.
/// </para>
/// <para>
/// There is no fixture committed to this repository for the same reason there is no signed
/// document: a chart Word wrote belongs to whoever wrote it. The corpus is a path on the
/// machine, and these tests skip when it is not there.
/// </para>
/// </remarks>
public class GraphChartCorpusTests
{
    private static readonly string CorpusRoot = ReferenceCorpus.Telerik;

    /// <summary>Every chart the corpus holds, read once.</summary>
    private static readonly Lazy<List<Found>> Charts = new(Scan);

    [Fact]
    public void TheCorpus_HoldsChartsWordWrote()
    {
        Assert.SkipWhen(!Directory.Exists(CorpusRoot), ReferenceCorpus.Absent);
        Assert.SkipWhen(Charts.Value.Count == 0, "The corpus holds no Microsoft Graph charts.");

        Assert.All(Charts.Value, static found => Assert.NotEmpty(found.Chart.Location));
    }

    /// <summary>
    /// The bar every chart in the corpus has to clear: it is recognised, it says what kind it
    /// is, and every series it lists has as many categories as it has values.
    /// </summary>
    [Fact]
    public void EveryChartInTheCorpus_ReadsAsSeriesWithMatchingCategories()
    {
        Assert.SkipWhen(!Directory.Exists(CorpusRoot), ReferenceCorpus.Absent);
        Assert.SkipWhen(Charts.Value.Count == 0, "The corpus holds no Microsoft Graph charts.");

        foreach (Found found in Charts.Value)
        {
            foreach (ChartSeries series in found.Chart.Series)
            {
                Assert.True(
                    series.Categories.Count == series.Values.Count,
                    $"{found.Path}: a series has {series.Values.Count} values and {series.Categories.Count} categories");
            }
        }
    }

    /// <summary>
    /// A trendline and an error bar are stored as series and are not series anybody wants in a
    /// list of them, so none may appear in one.
    /// </summary>
    [Fact]
    public void NoChartInTheCorpus_ListsATrendlineAmongItsSeries()
    {
        Assert.SkipWhen(!Directory.Exists(CorpusRoot), ReferenceCorpus.Absent);
        Assert.SkipWhen(Charts.Value.Count == 0, "The corpus holds no Microsoft Graph charts.");

        foreach (Found found in Charts.Value)
            Assert.All(found.Chart.Series, static series => Assert.NotEmpty(series.Values));
    }

    /// <summary>What the corpus turned out to hold, for the record.</summary>
    [Fact]
    public void TheChartsAreWorthWritingDown()
    {
        Assert.SkipWhen(!Directory.Exists(CorpusRoot), ReferenceCorpus.Absent);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{Charts.Value.Count} charts" + Environment.NewLine +
            string.Join(Environment.NewLine, Charts.Value.Select(static found =>
                $"  {Path.GetFileName(found.Path)}: {found.Chart.Kind}, " +
                $"{found.Chart.Series.Count} series, " +
                $"{string.Join('/', found.Chart.Series.Select(static s => s.Values.Count))} values")));

        Assert.True(true);
    }

    private static List<Found> Scan()
    {
        var found = new List<Found>();
        if (!Directory.Exists(CorpusRoot))
            return found;

        foreach (string path in Directory.EnumerateFiles(CorpusRoot, "*.doc", SearchOption.AllDirectories))
        {
            if (new FileInfo(path).Length is <= 0 or > 8 * 1024 * 1024)
                continue;

            try
            {
                WordDocument document = DocReader.Load(File.ReadAllBytes(path));
                found.AddRange(document.Charts
                    .Where(static chart => chart.Series.Count > 0)
                    .Select(chart => new Found(path, chart)));
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                // A file the reader declines has no charts to speak of.
            }
        }

        return found;
    }

    /// <summary>One chart and the file it came out of.</summary>
    /// <param name="Path">Where the document is.</param>
    /// <param name="Chart">What was read from it.</param>
    private readonly record struct Found(string Path, Chart Chart);
}
