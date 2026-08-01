using Quillwright.Editing;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Text boxes as containers: the words inside a shape are words of the document, reachable by
/// everything that reaches the rest of it.
/// </summary>
/// <remarks>
/// Word writes the same words twice, once as a modern drawing and once as the VML picture an
/// older reader falls back to. The model holds them once, so the two copies cannot drift
/// apart.
/// </remarks>
public class TextBoxTests
{
    private static readonly string[] CorpusRoots = ReferenceCorpus.Roots;

    /// <summary>A Word file with one anchored text box holding the words "TextFrame Story".</summary>
    private const string Fixture = "Comment015.docx";

    [Fact]
    public async Task TheWordsInATextBox_AreInTheDocumentsText()
    {
        WordDocument document = await FixtureAsync();

        Assert.Contains("TextFrame Story", document.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATextBox_IsAContainerLikeAnyOther()
    {
        WordDocument document = await FixtureAsync();

        TextBox box = document.AllContainers.OfType<TextBox>().Single();

        Assert.Equal("TextFrame Story", box.GetText());
        Assert.Same(document, box.Document);
    }

    [Fact]
    public async Task ReplacingText_ReachesInsideATextBox()
    {
        WordDocument document = await FixtureAsync();

        int replaced = document.Replace("TextFrame", "Caption");

        Assert.Equal(1, replaced);
        Assert.Contains("Caption Story", document.GetText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole point of modelling the two copies as one: an edit has to land in both, or a
    /// reader that uses the fallback shows the words as they were.
    /// </summary>
    [Fact]
    public async Task AnEditToATextBox_ReachesBothCopiesOfIt()
    {
        WordDocument document = await FixtureAsync();
        document.Replace("TextFrame", "Caption");

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        string part = OpenXmlAssert.ReadPart(saved, "word/document.xml");

        Assert.Equal(2, Occurrences(part, "Caption"));
        Assert.Equal(0, Occurrences(part, "TextFrame"));
        OpenXmlAssert.Valid(saved, "an edited text box");
    }

    /// <summary>The shape around the words is not modelled, so it has to come back untouched.</summary>
    [Fact]
    public async Task TheShapeAroundTheWords_SurvivesAnEditToThem()
    {
        WordDocument document = await FixtureAsync();
        document.Replace("TextFrame", "Caption");

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        string part = OpenXmlAssert.ReadPart(saved, "word/document.xml");

        Assert.Contains("<wps:bodyPr", part, StringComparison.Ordinal);
        Assert.Contains("vert=\"mongolianVert\"", part, StringComparison.Ordinal);
        Assert.Contains("<v:shapetype", part, StringComparison.Ordinal);
        Assert.Contains("mso-layout-flow-alt:top-to-bottom", part, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shape reports its own geometry, read off the markup it keeps. A renderer needs it, and
    /// reading it must not make the shape look edited.
    /// </summary>
    [Fact]
    public async Task AShape_SaysHowBigItIsAndWhereItSits()
    {
        WordDocument document = await FixtureAsync();
        Shape shape = document.Paragraphs
            .SelectMany(static p => p.Objects)
            .Select(static a => a.Object)
            .OfType<Shape>()
            .First();

        Assert.True(shape.Width.Twips > 0, "the shape reported no width");
        Assert.True(shape.Height.Twips > 0, "the shape reported no height");

        if (!shape.IsInline)
            Assert.NotNull(shape.Anchor);
    }

    [Fact]
    public async Task ATextBoxLeftAlone_IsSavedTheSameTwice()
    {
        WordDocument document = await FixtureAsync();
        using MemoryStream once = await DocumentFixture.SaveAsync(document);

        once.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(once, cancellationToken: TestContext.Current.CancellationToken);
        using MemoryStream twice = await DocumentFixture.SaveAsync(reloaded);

        Assert.Equal(
            OpenXmlAssert.ReadPart(once, "word/document.xml"),
            OpenXmlAssert.ReadPart(twice, "word/document.xml"));
    }

    /// <summary>
    /// The corpus is what proves the scan finds the shapes Word writes rather than only the
    /// one this test picked.
    /// </summary>
    [Fact]
    public async Task TextBoxes_AreFoundThroughoutTheCorpus()
    {
        List<TextBox> boxes = [];
        foreach (string path in Corpus())
        {
            try
            {
                WordDocument document = await WordDocument.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken);
                boxes.AddRange(document.AllContainers.OfType<TextBox>());
            }
            catch (Diagnostics.DocxFormatException)
            {
                // A corpus of test files includes deliberately corrupt ones.
            }
        }

        Assert.SkipWhen(boxes.Count == 0, ReferenceCorpus.Absent);
        Assert.True(boxes.Count >= 10, $"only {boxes.Count} text boxes were found");
        Assert.Contains(boxes, static box => box.GetText().Length > 0);
    }

    private static int Occurrences(string text, string value)
    {
        int count = 0;
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
