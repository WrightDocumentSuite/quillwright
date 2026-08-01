using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// A chart in a legacy document is an embedded Microsoft Graph object ([MS-OGRAPH]): a
/// compound file whose <c>Workbook</c> stream holds a grid of cells and a set of series
/// saying which line of that grid each of them draws.
/// </summary>
public class GraphChartTests
{
    [Fact]
    public void AGraphChart_YieldsItsSeriesAndValues()
    {
        Chart chart = Read(Sales());

        ChartSeries series = Assert.Single(chart.Series);
        Assert.Equal("Revenue", series.Name);
        Assert.Equal([120d, 95d, 143d], series.Values);
        Assert.Equal(["Q1", "Q2", "Q3"], series.Categories);
    }

    [Fact]
    public void TwoSeries_EachDrawTheirOwnRow()
    {
        var workbook = new GraphWorkbook();
        Header(workbook);
        Row(workbook, 1, "Revenue", 120, 95, 143);
        Row(workbook, 2, "Cost", 80, 70, 90);
        workbook.AddSeries("Revenue", values: 1, categories: 0);
        workbook.AddSeries("Cost", values: 2, categories: 0);

        Chart chart = Read(workbook);

        Assert.Equal(2, chart.Series.Count);
        Assert.Equal([80d, 70d, 90d], chart.Series[1].Values);
        Assert.Equal(["Q1", "Q2", "Q3"], chart.Series[1].Categories);
    }

    /// <summary>The other way round: a series is a column of the sheet, not a row of it.</summary>
    [Fact]
    public void SeriesArrangedByColumns_ReadTheColumnsInstead()
    {
        var workbook = new GraphWorkbook { SeriesInRows = false };
        workbook.Cell(0, 1, "Revenue");
        workbook.Cell(1, 0, "Q1").Cell(1, 1, 120);
        workbook.Cell(2, 0, "Q2").Cell(2, 1, 95);
        workbook.AddSeries("Revenue", values: 1, categories: 0);

        Chart chart = Read(workbook);

        Assert.Equal([120d, 95d], Assert.Single(chart.Series).Values);
        Assert.Equal(["Q1", "Q2"], chart.Series[0].Categories);
    }

    /// <summary>The name is cached on the series, but the sheet has it too when it is not.</summary>
    [Fact]
    public void ASeriesWithNoCachedName_TakesItFromTheSheet()
    {
        var workbook = new GraphWorkbook();
        Header(workbook);
        Row(workbook, 1, "Revenue", 120, 95, 143);
        workbook.AddSeries(name: null, values: 1, categories: 0);

        Assert.Equal("Revenue", Assert.Single(Read(workbook).Series).Name);
    }

    [Fact]
    public void AGapInTheData_ComesBackAsAGap()
    {
        var workbook = new GraphWorkbook();
        Header(workbook);
        workbook.Cell(1, 0, "Revenue").Cell(1, 1, 120).Cell(1, 3, 143);
        workbook.AddSeries("Revenue", values: 1, categories: 0);

        Assert.Equal([120d, null, 143d], Assert.Single(Read(workbook).Series).Values);
    }

    [Fact]
    public void TheChartsKind_ComesFromTheRecordThatNamesIt() =>
        Assert.Equal(ChartKind.Bar, Read(Sales()).Kind);

    /// <summary>
    /// A record too long for the format's maximum payload runs on into a <c>Continue</c>
    /// ([MS-OGRAPH] 2.4.23). A reader that stops at the first record reads the label truncated
    /// and says nothing about it, which is the worst way for this to go wrong.
    /// </summary>
    [Fact]
    public void ARecordSplitAcrossAContinue_IsReadAsOneRecord()
    {
        var workbook = new GraphWorkbook { SplitLongRecordsAt = 8 };
        workbook.Cell(0, 1, "A quarter with a long name");
        workbook.Cell(1, 0, "Revenue").Cell(1, 1, 120);
        workbook.AddSeries("Revenue", values: 1, categories: 0);

        Assert.Equal(["A quarter with a long name"], Assert.Single(Read(workbook).Series).Categories);
    }

    /// <summary>
    /// A trendline is written as a series and is not one: a caller asking a chart what it draws
    /// wants the data, not the line somebody fitted through it.
    /// </summary>
    [Fact]
    public void ATrendline_IsNotListedAmongTheSeries()
    {
        var workbook = new GraphWorkbook();
        Header(workbook);
        Row(workbook, 1, "Revenue", 120, 95, 143);
        workbook.AddSeries("Revenue", values: 1, categories: 0);
        workbook.AddSeries("Trend", values: 1, categories: 0, trendline: true);

        ChartSeries series = Assert.Single(Read(workbook).Series);
        Assert.Equal("Revenue", series.Name);
    }

    /// <summary>
    /// A chart that combines two kinds puts its series into groups and gives each group a kind
    /// of its own, so one kind for the whole chart is not an answer.
    /// </summary>
    [Fact]
    public void AChartCombiningTwoKinds_TellsEachSeriesApart()
    {
        var workbook = new GraphWorkbook();
        Header(workbook);
        Row(workbook, 1, "Revenue", 120, 95, 143);
        Row(workbook, 2, "Target", 100, 100, 100);
        workbook.AddSeries("Revenue", values: 1, categories: 0);
        workbook.AddSeries("Target", values: 2, categories: 0);
        workbook.AddGroup(GraphWorkbook.BarGroup, 1);
        workbook.AddGroup(GraphWorkbook.LineGroup, 2);

        Chart chart = Read(workbook);

        Assert.Equal(ChartKind.Bar, chart.Kind);
        Assert.Equal(ChartKind.Bar, chart.Series[0].Kind);
        Assert.Equal(ChartKind.Line, chart.Series[1].Kind);
    }

    /// <summary>A bubble is a third stream of numbers the model had nowhere to put.</summary>
    [Fact]
    public void ABubbleChart_KeepsTheSizesAsWellAsTheValues()
    {
        var workbook = new GraphWorkbook();
        Header(workbook);
        Row(workbook, 1, "Revenue", 120, 95, 143);
        Row(workbook, 2, "Weight", 3, 8, 5);
        workbook.AddSeries("Revenue", values: 1, categories: 0, bubbles: 2);
        workbook.AddGroup(GraphWorkbook.BubbleGroup, 1);

        Chart chart = Read(workbook);

        Assert.Equal(ChartKind.Bubble, chart.Kind);
        Assert.Equal([120d, 95d, 143d], chart.Series[0].Values);
        Assert.Equal([3d, 8d, 5d], chart.Series[0].BubbleSizes);
    }

    /// <summary>A chart that draws no bubbles has no sizes, rather than a column of nothing.</summary>
    [Fact]
    public void AChartThatDrawsNoBubbles_HasNoSizes() =>
        Assert.Empty(Assert.Single(Read(Sales()).Series).BubbleSizes);

    [Fact]
    public void AnObjectThatIsNotACompoundFile_IsNotAChart() =>
        Assert.Null(GraphChartReader.Read(Object([1, 2, 3, 4])));

    [Fact]
    public void AGraphObjectWithNoSeries_IsNotAChart() =>
        Assert.Null(GraphChartReader.Read(Object(new GraphWorkbook().Build())));

    private static GraphWorkbook Sales()
    {
        var workbook = new GraphWorkbook();
        Header(workbook);
        Row(workbook, 1, "Revenue", 120, 95, 143);
        workbook.AddSeries("Revenue", values: 1, categories: 0);
        return workbook;
    }

    private static void Header(GraphWorkbook workbook) =>
        workbook.Cell(0, 1, "Q1").Cell(0, 2, "Q2").Cell(0, 3, "Q3");

    private static void Row(GraphWorkbook workbook, int row, string name, params double[] values)
    {
        workbook.Cell(row, 0, name);
        for (int i = 0; i < values.Length; i++)
            workbook.Cell(row, i + 1, values[i]);
    }

    private static Chart Read(GraphWorkbook workbook) =>
        GraphChartReader.Read(Object(workbook.Build())) ?? throw new InvalidOperationException("The chart was not read.");

    private static EmbeddedObject Object(byte[] content) => new()
    {
        Location = "ObjectPool/_1",
        ProgramId = "MSGraph.Chart.8",
        DisplayName = "Chart",
        Content = content,
    };
}
