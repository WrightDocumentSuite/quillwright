using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// The parts of ISO/IEC 29500-1 §22.1 that were kept as bytes until they were modelled: the
/// framed box, the array of equations, the two limits, the phantom, and the display paragraph
/// that carries a justification or more than one equation.
/// </summary>
/// <remarks>
/// Each fixture is written the way the corresponding example in §22.1.2 is, so that a test
/// passes because the reader agrees with the specification rather than with the writer.
/// </remarks>
public class OfficeMathObjectTests
{
    private const string Namespace = " xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";

    [Fact]
    public async Task ABorderBox_KeepsWhichEdgesAreDrawnAndWhichLinesCrossIt()
    {
        MathObject equation = await ReadAsync(
            "<m:oMath><m:borderBox><m:borderBoxPr><m:hideTop m:val=\"1\"/><m:hideLeft m:val=\"1\"/>" +
            "<m:strikeBLTR m:val=\"1\"/></m:borderBoxPr>" +
            "<m:e><m:r><m:t>x+y</m:t></m:r></m:e></m:borderBox></m:oMath>");

        var box = Assert.IsType<MathBorderBox>(equation.Content.Nodes[0]);
        Assert.True(box.HideTop);
        Assert.True(box.HideLeft);
        Assert.True(box.StrikeUpward);
        Assert.False(box.HideBottom);
        Assert.False(box.StrikeDownward);
        Assert.Equal("x+y", box.GetText());
    }

    [Fact]
    public async Task AnArray_KeepsItsRowsInOrder()
    {
        MathObject equation = await ReadAsync(
            "<m:oMath><m:eqArr><m:e><m:r><m:t>x+y=1</m:t></m:r></m:e>" +
            "<m:e><m:r><m:t>x-y=0</m:t></m:r></m:e></m:eqArr></m:oMath>");

        var array = Assert.IsType<MathArray>(equation.Content.Nodes[0]);
        Assert.Equal(2, array.Rows.Count);
        Assert.Equal("x+y=1; x-y=0", array.GetText());
    }

    [Theory]
    [InlineData("limLow", "_")]
    [InlineData("limUpp", "^")]
    public async Task ALimit_SitsOnTheSideItsElementNames(string element, string separator)
    {
        MathObject equation = await ReadAsync(
            $"<m:oMath><m:{element}><m:e><m:r><m:t>lim</m:t></m:r></m:e>" +
            $"<m:lim><m:r><m:t>n\u2192\u221E</m:t></m:r></m:lim></m:{element}></m:oMath>");

        var limit = Assert.IsType<MathLimit>(equation.Content.Nodes[0]);
        Assert.Equal(element == "limUpp" ? MathEdge.Top : MathEdge.Bottom, limit.Position);
        Assert.Equal("lim" + separator + "(n\u2192\u221E)", limit.GetText());
    }

    /// <summary>
    /// A phantom takes room without being drawn, so it reads as nothing — unless it says its
    /// contents are shown, which is what <c>m:show</c> is for.
    /// </summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("<m:show m:val=\"1\"/>", "abc")]
    public async Task APhantom_ReadsAsWhateverItActuallyShows(string properties, string expected)
    {
        MathObject equation = await ReadAsync(
            $"<m:oMath><m:phant><m:phantPr>{properties}<m:zeroWid m:val=\"1\"/></m:phantPr>" +
            "<m:e><m:r><m:t>abc</m:t></m:r></m:e></m:phant></m:oMath>");

        var phantom = Assert.IsType<MathPhantom>(equation.Content.Nodes[0]);
        Assert.True(phantom.ZeroWidth);
        Assert.Equal(expected, phantom.GetText());
    }

    [Fact]
    public async Task ADisplayParagraph_KeepsItsJustification()
    {
        MathObject equation = await ReadAsync(
            "<m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>" +
            "<m:oMath><m:r><m:t>E=mc</m:t></m:r></m:oMath></m:oMathPara>");

        Assert.True(equation.IsDisplay);
        Assert.Equal(MathJustification.Left, equation.Justification);
        Assert.Equal("E=mc", equation.GetText());
    }

    /// <summary>
    /// A display paragraph may hold several equations, which used to make the whole thing a
    /// preserved fragment with no tree at all.
    /// </summary>
    [Fact]
    public async Task ADisplayParagraphOfSeveralEquations_ModelsEachOfThem()
    {
        MathObject equation = await ReadAsync(
            "<m:oMathPara><m:oMath><m:r><m:t>a=1</m:t></m:r></m:oMath>" +
            "<m:oMath><m:r><m:t>b=2</m:t></m:r></m:oMath></m:oMathPara>");

        Assert.Equal(2, equation.Equations.Count);
        Assert.Equal("a=1", equation.Content.GetText());
        Assert.Equal("b=2", equation.Equations[1].GetText());
        Assert.Equal("a=1 b=2", equation.GetText());
    }

    [Fact]
    public async Task EachNewObject_SurvivesBeingRegeneratedFromTheTree()
    {
        const string Markup =
            "<m:oMathPara><m:oMathParaPr><m:jc m:val=\"center\"/></m:oMathParaPr>" +
            "<m:oMath><m:borderBox><m:borderBoxPr><m:strikeH m:val=\"1\"/></m:borderBoxPr>" +
            "<m:e><m:r><m:t>a</m:t></m:r></m:e></m:borderBox>" +
            "<m:limUpp><m:e><m:r><m:t>max</m:t></m:r></m:e><m:lim><m:r><m:t>k</m:t></m:r></m:lim></m:limUpp>" +
            "<m:phant><m:phantPr><m:zeroAsc m:val=\"1\"/></m:phantPr><m:e><m:r><m:t>p</m:t></m:r></m:e></m:phant>" +
            "<m:eqArr><m:e><m:r><m:t>r1</m:t></m:r></m:e><m:e><m:r><m:t>r2</m:t></m:r></m:e></m:eqArr>" +
            "</m:oMath></m:oMathPara>";

        WordDocument document = await LoadAsync(Markup);
        MathObject equation = Equations(document).Single();
        equation.Invalidate();

        MathObject reopened = Equations(await ReloadAsync(document)).Single();

        Assert.Equal(MathJustification.Center, reopened.Justification);
        Assert.True(Assert.IsType<MathBorderBox>(reopened.Content.Nodes[0]).StrikeHorizontal);
        Assert.Equal(MathEdge.Top, Assert.IsType<MathLimit>(reopened.Content.Nodes[1]).Position);
        Assert.True(Assert.IsType<MathPhantom>(reopened.Content.Nodes[2]).ZeroAscent);
        Assert.Equal(2, Assert.IsType<MathArray>(reopened.Content.Nodes[3]).Rows.Count);
    }

    /// <summary>
    /// The rule for regenerated markup: the control properties an object arrived with are
    /// carried, because they are the formatting of the character it is drawn around, and
    /// losing them is the difference between an italic variable and an upright one.
    /// </summary>
    [Fact]
    public async Task RegeneratingAnEquation_KeepsTheControlPropertiesOfEveryObject()
    {
        const string Markup =
            "<m:oMath><m:f><m:fPr><m:ctrlPr><w:rPr><w:i/></w:rPr></m:ctrlPr></m:fPr>" +
            "<m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>2</m:t></m:r></m:den></m:f>" +
            "<m:sSup><m:sSupPr><w:ctrlPr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "</w:ctrlPr></m:sSupPr><m:e><m:r><m:t>e</m:t></m:r></m:e>" +
            "<m:sup><m:r><m:t>x</m:t></m:r></m:sup></m:sSup></m:oMath>";

        WordDocument document = await LoadAsync(Markup);
        MathObject equation = Equations(document).Single();

        var fraction = Assert.IsType<MathFraction>(equation.Content.Nodes[0]);
        Assert.NotNull(fraction.ControlPropertiesXml);

        // A control-properties element in the wrong namespace is not one, and is left alone.
        Assert.Null(Assert.IsType<MathScript>(equation.Content.Nodes[1]).ControlPropertiesXml);

        equation.Invalidate();
        string markup = await MarkupAsync(document);

        int start = markup.IndexOf("<m:fPr>", StringComparison.Ordinal);
        int end = markup.IndexOf("</m:fPr>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "the fraction was written without its properties");

        string properties = markup[start..end];
        Assert.Contains("ctrlPr", properties, StringComparison.Ordinal);
        Assert.Contains("<w:i", properties, StringComparison.Ordinal);
    }

    /// <summary>
    /// The separator of a delimiter is written between the two brackets, which is where the
    /// schema declares it and not where a reader would expect it.
    /// </summary>
    [Fact]
    public async Task ADelimiterWrittenFromTheTree_PutsItsCharactersInSchemaOrder()
    {
        var delimiter = new MathDelimiter { Begin = "{", End = "}", Separator = ";" };
        delimiter.Arguments.Add(MathElement.Of("a"));
        delimiter.Arguments.Add(MathElement.Of("b"));

        var equation = new MathObject();
        equation.Content.Nodes.Add(delimiter);

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(equation);
        string markup = await MarkupAsync(document);

        Assert.Contains(
            "<m:begChr m:val=\"{\"/><m:sepChr m:val=\";\"/><m:endChr m:val=\"}\"/>",
            markup,
            StringComparison.Ordinal);
    }

    private static IEnumerable<MathObject> Equations(WordDocument document) =>
        document.AllContainers
            .SelectMany(static container => container.Blocks.Paragraphs)
            .SelectMany(static paragraph => paragraph.Objects)
            .Select(static anchored => anchored.Object)
            .OfType<MathObject>();

    private static async Task<MathObject> ReadAsync(string equation) =>
        Equations(await LoadAsync(equation)).Single();

    private static async Task<WordDocument> LoadAsync(string equation)
    {
        string declared = equation
            .Replace("<m:oMath>", "<m:oMath" + Namespace + ">", StringComparison.Ordinal)
            .Replace("<m:oMathPara>", "<m:oMathPara" + Namespace + ">", StringComparison.Ordinal);

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(new RawInline(declared, isRunChild: false));
        return await ReloadAsync(document);
    }

    private static async Task<string> MarkupAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "equation markup");
        return OpenXmlAssert.ReadPart(buffer, "document.xml");
    }

    private static async Task<WordDocument> ReloadAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        buffer.Position = 0;
        return await WordDocument.LoadAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
    }
}
