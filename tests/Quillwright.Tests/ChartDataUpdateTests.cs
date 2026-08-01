using System.Text;
using System.Xml;
using Quillwright.Formats;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Rewriting the data a chart draws: the caches under workbook references become literals, the
/// look of the chart is untouched, and the rewritten part reads back as the new numbers.
/// </summary>
public class ChartDataUpdateTests
{
    private const string ChartNamespace = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static readonly string BarChartPart =
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <c:chartSpace xmlns:c="{ChartNamespace}">
         <c:chart>
          <c:plotArea>
           <c:layout/>
           <c:barChart>
            <c:barDir val="col"/>
            <c:ser>
             <c:idx val="0"/>
             <c:order val="0"/>
             <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>Old name</c:v></c:pt></c:strCache></c:strRef></c:tx>
             <c:spPr/>
             <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$3</c:f><c:strCache><c:ptCount val="2"/><c:pt idx="0"><c:v>Alpha</c:v></c:pt><c:pt idx="1"><c:v>Beta</c:v></c:pt></c:strCache></c:strRef></c:cat>
             <c:val><c:numRef><c:f>Sheet1!$B$2:$B$3</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="2"/><c:pt idx="0"><c:v>1</c:v></c:pt><c:pt idx="1"><c:v>2</c:v></c:pt></c:numCache></c:numRef></c:val>
            </c:ser>
           </c:barChart>
          </c:plotArea>
         </c:chart>
        </c:chartSpace>
        """;

    [Fact]
    public void TheRewrittenPart_ReadsBackAsTheNewData()
    {
        byte[] rewritten = ChartDataWriter.Rewrite(
            Encoding.UTF8.GetBytes(BarChartPart),
            [new ChartSeries { Name = "Revenue", Categories = ["Q1", "Q2", "Q3"], Values = [10.5, null, 30] }]);

        Chart chart = Read(rewritten);
        ChartSeries series = Assert.Single(chart.Series);
        Assert.Equal("Revenue", series.Name);
        Assert.Equal(["Q1", "Q2", "Q3"], series.Categories);
        Assert.Equal([10.5, 30], series.Values);
    }

    /// <summary>
    /// The data becomes literal: after the rewrite the chart's numbers live in the chart, and
    /// no formula points into a workbook that still holds the old ones.
    /// </summary>
    [Fact]
    public void TheWorkbookReference_IsGoneFromTheRewrittenData()
    {
        byte[] rewritten = ChartDataWriter.Rewrite(
            Encoding.UTF8.GetBytes(BarChartPart),
            [new ChartSeries { Categories = ["Q1", "Q2"], Values = [1, 2] }]);

        string xml = Encoding.UTF8.GetString(rewritten);
        Assert.DoesNotContain("Sheet1!$A$2", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Sheet1!$B$2", xml, StringComparison.Ordinal);
        Assert.Contains("strLit", xml, StringComparison.Ordinal);
        Assert.Contains("numLit", xml, StringComparison.Ordinal);

        // The name was not given, so the reference that names the series survives.
        Assert.Contains("Sheet1!$B$1", xml, StringComparison.Ordinal);
        Assert.Contains("Old name", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLook_IsUntouched()
    {
        byte[] rewritten = ChartDataWriter.Rewrite(
            Encoding.UTF8.GetBytes(BarChartPart),
            [new ChartSeries { Categories = ["Q1"], Values = [1] }]);

        string xml = Encoding.UTF8.GetString(rewritten);
        Assert.Contains("<c:barDir val=\"col\"", xml, StringComparison.Ordinal);
        Assert.Contains("<c:spPr", xml, StringComparison.Ordinal);
        Assert.Contains("<c:layout", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void NumericCategories_StayANumberAxis()
    {
        byte[] rewritten = ChartDataWriter.Rewrite(
            Encoding.UTF8.GetBytes(BarChartPart),
            [new ChartSeries { Categories = ["1.5", "2.5"], Values = [1, 2] }]);

        string xml = Encoding.UTF8.GetString(rewritten);
        Assert.DoesNotContain("strLit", xml, StringComparison.Ordinal);

        Chart chart = Read(rewritten);
        Assert.Equal(["1.5", "2.5"], chart.Series[0].Categories);
    }

    [Fact]
    public void TheSeriesCount_MustMatchTheChart()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => ChartDataWriter.Rewrite(
            Encoding.UTF8.GetBytes(BarChartPart),
            [new ChartSeries(), new ChartSeries()]));

        Assert.Contains("1 series", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AChartOfAnotherDocument_IsRefused()
    {
        WordDocument document = WordDocument.Create();
        var foreign = new Chart { Location = "/word/charts/chart1.xml" };

        Assert.Throws<ArgumentException>(() => document.SetChartData(foreign, []));
    }

    private static Chart Read(byte[] content)
    {
        using var xml = XmlReader.Create(new MemoryStream(content), Quillwright.Xml.XmlDefaults.ReaderSettings);
        return ChartPartReader.Read(xml, "/word/charts/chart1.xml");
    }
}
