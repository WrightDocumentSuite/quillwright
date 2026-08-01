using System.Text;
using Quillwright.Model;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Charts on the page, drawn from the numbers the document cached for them.
/// </summary>
/// <remarks>
/// A chart is a drawing in the body that names a part, and the part holds the numbers. Both
/// halves are built here in markup, because a chart assembled through the model would not
/// exercise the join between the two — which is where a chart goes missing.
/// </remarks>
public sealed class ChartTests
{
    private const string ChartPart = "/word/charts/chart1.xml";

    private static string Bars(params (string Category, double Value)[] points) => Part("barChart", points);

    private static string Part(string kind, (string Category, double Value)[] points, string? title = "Sales")
    {
        var markup = new StringBuilder();
        markup.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        markup.Append("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" ");
        markup.Append("xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><c:chart>");

        if (title is not null)
            markup.Append($"<c:title><c:tx><c:rich><a:p><a:r><a:t>{title}</a:t></a:r></a:p></c:rich></c:tx></c:title>");

        markup.Append($"<c:plotArea><c:{kind}><c:ser><c:tx><c:strRef><c:strCache><c:pt idx=\"0\">");
        markup.Append("<c:v>Region</c:v></c:pt></c:strCache></c:strRef></c:tx>");

        markup.Append("<c:cat><c:strRef><c:strCache>");
        for (int i = 0; i < points.Length; i++)
            markup.Append($"<c:pt idx=\"{i}\"><c:v>{points[i].Category}</c:v></c:pt>");
        markup.Append("</c:strCache></c:strRef></c:cat>");

        markup.Append("<c:val><c:numRef><c:numCache>");
        for (int i = 0; i < points.Length; i++)
            markup.Append($"<c:pt idx=\"{i}\"><c:v>{points[i].Value}</c:v></c:pt>");
        markup.Append("</c:numCache></c:numRef></c:val>");

        markup.Append($"</c:ser></c:{kind}></c:plotArea></c:chart></c:chartSpace>");
        return markup.ToString();
    }

    /// <summary>The drawing that reserves room for the chart and names its relationship.</summary>
    private static string Frame(long width = 3_600_000, long height = 2_400_000) =>
        "<w:drawing xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        $"<wp:inline><wp:extent cx=\"{width}\" cy=\"{height}\"/><wp:docPr id=\"1\" name=\"Chart 1\"/>" +
        "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">" +
        "<c:chart r:id=\"rIdChart\"/></a:graphicData></a:graphic></wp:inline></w:drawing>";

    /// <summary>
    /// Builds a package holding a body that draws a chart and the chart part behind it, then
    /// reads it back the way any other document is read.
    /// </summary>
    private static async Task<WordDocument> LoadAsync(string chartPart, string? frame = null)
    {
        WordDocument seed = WordDocument.Create();
        seed.Sections[0].AddParagraph("Before the chart.");

        var buffer = new MemoryStream();
        await seed.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);

        byte[] rebuilt = Packages.With(buffer.ToArray(), ChartPart, chartPart, frame ?? Frame());

        return await WordDocument.LoadAsync(
            new MemoryStream(rebuilt), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AChartPart_IsJoinedToTheFrameThatDrawsIt()
    {
        WordDocument document = await LoadAsync(Bars(("North", 3), ("South", 5)));

        Chart chart = Assert.Single(document.Charts);
        Assert.Equal(ChartPart, chart.Location);

        ChartFrame frame = document.Sections[0].Blocks.Paragraphs
            .SelectMany(static p => p.Objects)
            .Select(static anchored => anchored.Object)
            .OfType<ChartFrame>()
            .Single();

        Assert.Equal(ChartPart, frame.Location);
        Assert.True(frame.IsInline);
        Assert.InRange(frame.Width.Points, 283, 284);
        Assert.InRange(frame.Height.Points, 188, 190);
    }

    [Fact]
    public async Task ABarChart_DrawsOneBarPerValue()
    {
        WordDocument document = await LoadAsync(Bars(("North", 3), ("South", 5), ("East", 2)));

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        // Three bars, each a filled quadrilateral: four points and a fill apiece.
        Assert.Equal(3, content.Split('\n').Count(static line => line == "f"));
        Assert.Contains("North", string.Concat(rendered.Lines()), StringComparison.Ordinal);
        Assert.Contains("Sales", string.Concat(rendered.Lines()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABarChart_RulesAndLabelsItsValueAxis()
    {
        WordDocument document = await LoadAsync(Bars(("A", 10), ("B", 20)));

        using Rendered rendered = Rendered.Of(document);
        string page = string.Concat(rendered.Lines());

        Assert.Contains("20", page, StringComparison.Ordinal);
        Assert.Contains("0", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALineChart_DrawsAPolylineRatherThanBars()
    {
        WordDocument document = await LoadAsync(Part("lineChart", [("A", 1), ("B", 4), ("C", 2)]));

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.DoesNotContain(content.Split('\n'), static line => line == "f");
        Assert.True(content.Split('\n').Count(static line => line == "S") >= 2, "no line was stroked for the series");
    }

    [Fact]
    public async Task APieChart_DrawsOneWedgePerValue()
    {
        WordDocument document = await LoadAsync(Part("pieChart", [("A", 1), ("B", 1), ("C", 2)]));

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.Equal(3, content.Split('\n').Count(static line => line == "f"));
    }

    [Fact]
    public async Task AChartOfAKindThatCannotBeDrawn_KeepsItsSpaceAndSaysSo()
    {
        WordDocument document = await LoadAsync(Part("surfaceChart", [("A", 1), ("B", 2)]));

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains(
            rendered.Diagnostics,
            warning => warning.Kind == PdfExportWarningKind.ContentSkipped && warning.Subject == "Surface");
    }

    /// <summary>
    /// Reading a chart adds a typed view and takes nothing away: the drawing is written back as
    /// the markup the reader captured, reference and size intact, because nothing in the model
    /// is allowed to author one.
    /// </summary>
    [Fact]
    public async Task AChartFrame_IsWrittenBackAsItWasRead()
    {
        WordDocument document = await LoadAsync(Bars(("A", 1)));

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        string markup = Packages.Part(buffer.ToArray(), "word/document.xml");

        Assert.Contains("<c:chart r:id=\"rIdChart\" />", markup, StringComparison.Ordinal);
        Assert.Contains("cx=\"3600000\"", markup, StringComparison.Ordinal);
        Assert.Contains("uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"", markup, StringComparison.Ordinal);
    }
}
