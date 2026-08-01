using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// The equation tree (ISO/IEC 29500-1 §22.1): reading the structures that carry meaning,
/// keeping the rest verbatim, and writing an untouched equation back as it arrived.
/// </summary>
public class OfficeMathModelTests
{
    private const string Namespace = " xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";

    private const string Fraction =
        "<m:oMath><m:r><m:t>x=</m:t></m:r><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num>" +
        "<m:den><m:r><m:t>2</m:t></m:r></m:den></m:f></m:oMath>";

    [Fact]
    public async Task AnEquation_ArrivesAsATree()
    {
        MathObject equation = await ReadAsync(Fraction);

        Assert.Equal(2, equation.Content.Nodes.Count);
        Assert.Equal("x=", Assert.IsType<MathRun>(equation.Content.Nodes[0]).Text);

        var fraction = Assert.IsType<MathFraction>(equation.Content.Nodes[1]);
        Assert.Equal("1", fraction.Numerator.GetText());
        Assert.Equal("2", fraction.Denominator.GetText());
        Assert.Equal(MathFractionKind.Bar, fraction.Kind);
    }

    [Fact]
    public async Task AnEquation_ReadsAsALineOfText()
    {
        WordDocument document = await LoadAsync(Fraction);

        Assert.Equal("x=1/2", document.GetText().Trim());
    }

    /// <summary>
    /// The point of keeping the original markup: everything §22.1 says that the tree does not
    /// hold has to survive, and the only way to be sure is to write the same bytes back.
    /// </summary>
    [Fact]
    public async Task AnUntouchedEquation_IsWrittenBackByteForByte()
    {
        const string Rich =
            "<m:oMath><m:sSup><m:sSupPr><m:ctrlPr><w:rPr><w:i/></w:rPr></m:ctrlPr></m:sSupPr>" +
            "<m:e><m:r><m:t>e</m:t></m:r></m:e><m:sup><m:r><m:t>x</m:t></m:r></m:sup></m:sSup></m:oMath>";

        string once = await MarkupAsync(await LoadAsync(Rich));
        string twice = await MarkupAsync(await LoadAsync(once[once.IndexOf("<m:oMath", StringComparison.Ordinal)..]));

        Assert.Contains("<m:ctrlPr>", once, StringComparison.Ordinal);
        Assert.Equal(Equation(once), Equation(twice));
    }

    [Theory]
    [InlineData("<m:sSub><m:e><m:r><m:t>a</m:t></m:r></m:e><m:sub><m:r><m:t>n</m:t></m:r></m:sub></m:sSub>", "a_n")]
    [InlineData("<m:sSup><m:e><m:r><m:t>e</m:t></m:r></m:e><m:sup><m:r><m:t>x</m:t></m:r></m:sup></m:sSup>", "e^x")]
    [InlineData(
        "<m:sSubSup><m:e><m:r><m:t>x</m:t></m:r></m:e><m:sub><m:r><m:t>i</m:t></m:r></m:sub>" +
        "<m:sup><m:r><m:t>2</m:t></m:r></m:sup></m:sSubSup>",
        "x_i^2")]
    [InlineData(
        "<m:rad><m:radPr><m:degHide m:val=\"1\"/></m:radPr><m:deg/><m:e><m:r><m:t>2</m:t></m:r></m:e></m:rad>",
        "\u221A2")]
    [InlineData(
        "<m:d><m:dPr><m:begChr m:val=\"[\"/><m:endChr m:val=\"]\"/></m:dPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>",
        "[x]")]
    [InlineData(
        "<m:func><m:fName><m:r><m:t>sin</m:t></m:r></m:fName><m:e><m:r><m:t>x</m:t></m:r></m:e></m:func>",
        "sin x")]
    public async Task EachStructure_ReadsAsTheLineItStandsFor(string inner, string expected)
    {
        MathObject equation = await ReadAsync("<m:oMath>" + inner + "</m:oMath>");

        Assert.Equal(expected, equation.GetText());
    }

    [Fact]
    public async Task ASumOverARange_KeepsItsOperatorAndLimits()
    {
        MathObject equation = await ReadAsync(
            "<m:oMath><m:nary><m:naryPr><m:chr m:val=\"\u2211\"/></m:naryPr>" +
            "<m:sub><m:r><m:t>i=1</m:t></m:r></m:sub><m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
            "<m:e><m:r><m:t>i</m:t></m:r></m:e></m:nary></m:oMath>");

        var sum = Assert.IsType<MathNary>(equation.Content.Nodes[0]);
        Assert.Equal("\u2211", sum.Operator);
        Assert.Equal("i=1", sum.Lower.GetText());
        Assert.Equal("n", sum.Upper.GetText());
        Assert.Equal("\u2211_(i=1)^n i", sum.GetText());
    }

    [Fact]
    public async Task AMatrix_KeepsItsRowsAndCells()
    {
        MathObject equation = await ReadAsync(
            "<m:oMath><m:m><m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e><m:e><m:r><m:t>b</m:t></m:r></m:e></m:mr>" +
            "<m:mr><m:e><m:r><m:t>c</m:t></m:r></m:e><m:e><m:r><m:t>d</m:t></m:r></m:e></m:mr></m:m></m:oMath>");

        var matrix = Assert.IsType<MathMatrix>(equation.Content.Nodes[0]);
        Assert.Equal(2, matrix.Rows.Count);
        Assert.Equal("(a, b; c, d)", matrix.GetText());
    }

    /// <summary>
    /// An element outside the modelled set keeps its own bytes and its own text. Every object
    /// §22.1 declares is now modelled, so what is left to preserve is the WordprocessingML an
    /// equation is allowed to hold — a tracked insertion here — which belongs to §17.
    /// </summary>
    [Fact]
    public async Task AnUnmodelledElement_IsKeptVerbatim()
    {
        MathObject equation = await ReadAsync(
            "<m:oMath><w:ins w:id=\"1\" w:author=\"Ada\"><m:r><m:t>hidden</m:t></m:r></w:ins></m:oMath>");

        var preserved = Assert.IsType<RawMath>(equation.Content.Nodes[0]);
        Assert.Contains("w:ins", preserved.Xml, StringComparison.Ordinal);
        Assert.Equal("hidden", preserved.Text);
    }

    [Fact]
    public async Task ABuiltEquation_ValidatesAndReadsBack()
    {
        var fraction = new MathFraction();
        fraction.Numerator.Nodes.Add(new MathRun("a+b"));
        fraction.Denominator.Nodes.Add(new MathRun("2"));

        var equation = new MathObject();
        equation.Content.Nodes.Add(new MathRun("y="));
        equation.Content.Nodes.Add(fraction);

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(equation);

        WordDocument reopened = await DocumentFixture.RoundTripAsync(document, "a built equation");

        Assert.Equal("y=(a+b)/2", Equations(reopened).Single().GetText());
    }

    [Fact]
    public async Task ADisplayEquation_StaysOnALineOfItsOwn()
    {
        var equation = new MathObject { IsDisplay = true };
        equation.Content.Nodes.Add(new MathRun("E=mc"));

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(equation);

        string markup = await MarkupAsync(document);

        Assert.Contains("<m:oMathPara>", markup, StringComparison.Ordinal);
        Assert.True(Equations(await ReloadAsync(document)).Single().IsDisplay);
    }

    /// <summary>Editing the tree gives up the original bytes, which is what makes the edit show.</summary>
    [Fact]
    public async Task AnEditedEquation_IsRegeneratedFromTheTree()
    {
        WordDocument document = await LoadAsync(Fraction);
        MathObject equation = Equations(document).Single();

        var fraction = (MathFraction)equation.Content.Nodes[1];
        ((MathRun)fraction.Denominator.Nodes[0]).Text = "3";
        equation.Invalidate();

        string markup = await MarkupAsync(document);

        Assert.Contains("<m:den><m:r><m:t>3</m:t></m:r></m:den>", markup, StringComparison.Ordinal);
        Assert.Equal("x=1/3", Equations(await ReloadAsync(document)).Single().GetText());
    }

    /// <summary>An equation is a child of the paragraph, not of a run, wherever it ends up.</summary>
    [Fact]
    public async Task AnEquation_KeepsItsPlaceBetweenTheRunsAroundIt()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("before ");
        paragraph.AppendObject(new MathObject { Content = { Nodes = { new MathRun("x") } } });
        paragraph.AppendText(" after");

        string markup = await MarkupAsync(document);
        int before = markup.IndexOf("before", StringComparison.Ordinal);
        int equation = markup.IndexOf("<m:oMath>", StringComparison.Ordinal);
        int after = markup.IndexOf("after", StringComparison.Ordinal);

        Assert.True(before < equation && equation < after, $"positions were {before}, {equation}, {after}");
        Assert.DoesNotContain("<w:r><m:oMath>", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The equations Word itself wrote, which use far more of §22.1 than any fixture here.
    /// The bar is that they are modelled and that a round trip leaves them saying the same
    /// thing.
    /// </summary>
    [Fact]
    public async Task AcrossTheCorpus_EquationsAreModelledAndSurviveARoundTrip()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        int found = 0;

        foreach (string path in ReferenceCorpus.Files("*.docx"))
        {
            WordDocument document;
            try
            {
                document = await WordDocument.LoadAsync(path, cancellationToken: cancellationToken);
            }
            catch (Quillwright.Diagnostics.DocxFormatException)
            {
                continue;
            }

            string[] before = [.. Equations(document).Select(static e => e.GetText() ?? string.Empty)];
            if (before.Length == 0)
                continue;

            found++;
            string[] after = [.. Equations(await ReloadAsync(document)).Select(static e => e.GetText() ?? string.Empty)];
            Assert.Equal(before, after);
        }

        Assert.SkipWhen(found == 0, ReferenceCorpus.Absent);
    }

    /// <summary>
    /// A Strict package names the math namespace under <c>purl.oclc.org</c>. It is the same
    /// vocabulary spelled differently, so an equation in one has to be modelled rather than
    /// preserved as markup nobody can read into a tree.
    /// </summary>
    [Fact]
    public async Task AnEquationInAStrictPackage_IsModelledLikeAnyOther()
    {
        string strictRoot = ReferenceCorpus.RequireOpenXmlPath(
            "test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/O14ISOStrict/Word");

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var modelled = new List<string>();
        foreach (string path in Directory.EnumerateFiles(strictRoot, "argSz-*.docx"))
        {
            WordDocument document = await WordDocument.LoadAsync(path, cancellationToken: cancellationToken);
            modelled.AddRange(Equations(document).Select(static e => e.GetText() ?? string.Empty));
        }

        Assert.SkipWhen(modelled.Count == 0, "The Strict corpus holds no equations.");
        Assert.All(modelled, static text => Assert.NotEmpty(text));
    }

    /// <summary>An untouched Strict equation goes back out as the bytes it arrived as.</summary>
    [Fact]
    public async Task AnUntouchedStrictEquation_IsByteStable()
    {
        string strictRoot = ReferenceCorpus.RequireOpenXmlPath(
            "test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/O14ISOStrict/Word");

        string? path = Directory.EnumerateFiles(strictRoot, "argSz-*.docx").FirstOrDefault();
        Assert.SkipWhen(path is null, "The Strict corpus holds no equations.");

        WordDocument document = await WordDocument.LoadAsync(path!, cancellationToken: TestContext.Current.CancellationToken);
        MathObject equation = Equations(document).First();

        Assert.NotNull(equation.OriginalXml);
        Assert.False(equation.IsDirty);

        WordDocument reopened = await ReloadAsync(document);
        Assert.Equal(equation.OriginalXml, Equations(reopened).First().OriginalXml);
    }

    private static IEnumerable<MathObject> Equations(WordDocument document) =>
        document.AllContainers
            .SelectMany(static container => container.Blocks.Paragraphs)
            .SelectMany(static paragraph => paragraph.Objects)
            .Select(static anchored => anchored.Object)
            .OfType<MathObject>();

    private static async Task<MathObject> ReadAsync(string equation) =>
        Equations(await LoadAsync(equation)).Single();

    /// <summary>Puts a fragment of equation markup into a package and reads it back out.</summary>
    private static async Task<WordDocument> LoadAsync(string equation)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(
            new RawInline(equation.Replace("<m:oMath>", "<m:oMath" + Namespace + ">", StringComparison.Ordinal), isRunChild: false));

        return await ReloadAsync(document);
    }

    private static string Equation(string markup) =>
        markup[markup.IndexOf("<m:oMath", StringComparison.Ordinal)..
            (markup.IndexOf("</m:oMath>", StringComparison.Ordinal) + "</m:oMath>".Length)];

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
