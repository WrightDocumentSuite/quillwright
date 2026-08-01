using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quillwright.Diagnostics;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// What the binary format carries and this reader does not, and what it now carries across
/// that it used to drop: text boxes keep their place, while a shape of any other kind and a
/// picture stored in a form the reader cannot decode are reported rather than lost quietly.
/// </summary>
public class DocConversionLossTests
{
    private static readonly string CorpusRoot = ReferenceCorpus.Telerik;

    /// <summary>Three text boxes of numbered lines around a two-word body, and a watermark shape.</summary>
    private const string TextBoxes = "IncorrectCreationOfWatermarkFromTextBox.doc";

    [Fact]
    public void TextBoxText_IsInTheDocumentsTextRatherThanLost()
    {
        WordDocument document = Fixture(TextBoxes);

        string text = document.GetText();
        Assert.Contains("hello", text, StringComparison.Ordinal);
        Assert.Contains("Text 1", text, StringComparison.Ordinal);
        Assert.Contains("Text 14", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The words of a text box used to be piled at the end of the document. Now the anchor in
    /// the text says where the box belongs, so the box is there.
    /// </summary>
    [Fact]
    public void ATextBox_IsAContainerAnchoredWhereItBelongs()
    {
        WordDocument document = Fixture(TextBoxes);

        List<Model.TextBox> boxes = [.. document.AllContainers.OfType<Model.TextBox>()];

        Assert.NotEmpty(boxes);
        Assert.Contains(boxes, static box => box.GetText().Contains("Text 1", StringComparison.Ordinal));
        Assert.All(boxes, static box => Assert.NotNull(box.Document));
    }

    [Fact]
    public void ARebuiltTextBox_SaysItsShapeWasNotConverted()
    {
        WordDocument document = Fixture(TextBoxes);

        Assert.Single(document.LoadDiagnostics, static warning =>
            warning.Code == WarningCode.PreservedVerbatim &&
            warning.Message.Contains("text box was rebuilt", StringComparison.Ordinal));
    }

    /// <summary>
    /// One warning however many shapes there are: the document has one thing missing from it,
    /// and forty copies of the same sentence would bury the others. The watermark documents in
    /// the corpus carry the same WordArt on every page, so they are where this shows.
    /// </summary>
    [Fact]
    public void ADrawingLossFoundManyTimes_IsReportedOncePerDocument()
    {
        List<DocumentWarning> warnings =
            FirstDocumentWarning(static w => w.Message.Contains("WordArt", StringComparison.Ordinal));

        Assert.SkipWhen(warnings.Count == 0, ReferenceCorpus.Absent);
        Assert.Single(warnings, static w => w.Message.Contains("WordArt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADocumentWithTextBoxes_StillConvertsToAValidPackage()
    {
        WordDocument document = Fixture(TextBoxes);

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);

        AssertValid(buffer);
    }

    /// <summary>
    /// A text box that came out of the binary format has to survive the trip into a package
    /// and back, or the shape it was rebuilt as is not a text box at all.
    /// </summary>
    [Fact]
    public async Task ARebuiltTextBox_IsStillATextBoxAfterASave()
    {
        WordDocument document = Fixture(TextBoxes);

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        buffer.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(
            reloaded.AllContainers.OfType<Model.TextBox>(),
            static box => box.GetText().Contains("Text 1", StringComparison.Ordinal));
    }

    /// <summary>
    /// The corpus is what proves the warnings fire on files this library did not write. Each
    /// kind has to appear somewhere in it, or the code raising it is never exercised.
    /// </summary>
    [Fact]
    public void EveryKindOfLoss_IsFoundSomewhereInTheCorpus()
    {
        List<DocumentWarning> warnings = [];
        foreach (WordDocument document in Corpus())
            warnings.AddRange(document.LoadDiagnostics);

        Assert.SkipWhen(warnings.Count == 0, ReferenceCorpus.Absent);
        Assert.Contains(warnings, static w => w.Message.Contains("text box was rebuilt", StringComparison.Ordinal));
        Assert.Contains(warnings, static w => w.Message.Contains("WordArt", StringComparison.Ordinal));
        Assert.Contains(warnings, static w => w.Code == WarningCode.UnresolvedMedia);

        // The corpus no longer raises the unconverted-shape warning at all: every shape in it
        // is a picture, a text box or lettering, and all three now come across. The warning
        // stays for the shapes a document outside the corpus can hold.
        Assert.DoesNotContain(warnings, static w => w.Message.Contains("does not convert", StringComparison.Ordinal));
    }

    /// <summary>
    /// A document that customises the toolbars or the keyboard says so. Those customisations
    /// are a table of Word's own command identifiers ([MS-CTDOC]) that no later format has
    /// anywhere to put, so the only honest thing to do is name the loss.
    /// </summary>
    [Fact]
    public void ACustomisedToolbar_IsReportedRatherThanDroppedQuietly()
    {
        List<DocumentWarning> warnings = [];
        foreach (WordDocument document in Corpus())
            warnings.AddRange(document.LoadDiagnostics);

        Assert.SkipWhen(warnings.Count == 0, ReferenceCorpus.Absent);
        Assert.Contains(warnings, static w =>
            w.Code == WarningCode.PreservedVerbatim &&
            w.Message.Contains("customises toolbars", StringComparison.Ordinal));
    }

    /// <summary>All the warnings of the first corpus document that raises one the filter accepts.</summary>
    private static List<DocumentWarning> FirstDocumentWarning(Func<DocumentWarning, bool> wanted)
    {
        foreach (WordDocument document in Corpus())
        {
            if (document.LoadDiagnostics.Any(wanted))
                return [.. document.LoadDiagnostics];
        }

        return [];
    }

    private static IEnumerable<WordDocument> Corpus()
    {
        if (!Directory.Exists(CorpusRoot))
            yield break;

        foreach (string path in Directory.EnumerateFiles(CorpusRoot, "*.doc", SearchOption.AllDirectories))
        {
            if (new FileInfo(path).Length is not (> 0 and < 8 * 1024 * 1024))
                continue;

            WordDocument document;
            try
            {
                document = DocReader.Load(File.ReadAllBytes(path));
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                // Encrypted and pre-Word 97 files are refused rather than warned about.
                continue;
            }

            yield return document;
        }
    }

    private static WordDocument Fixture(string name)
    {
        string path = Directory.Exists(CorpusRoot)
            ? Directory.EnumerateFiles(CorpusRoot, name, SearchOption.AllDirectories).FirstOrDefault() ?? name
            : name;

        Assert.SkipUnless(File.Exists(path), ReferenceCorpus.Absent);
        return DocReader.Load(File.ReadAllBytes(path));
    }

    private static void AssertValid(MemoryStream package)
    {
        package.Position = 0;
        using WordprocessingDocument saved = WordprocessingDocument.Open(package, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        ValidationErrorInfo[] errors = [.. validator.Validate(saved)];
        Assert.True(
            errors.Length == 0,
            string.Join(Environment.NewLine, errors.Take(5).Select(static e => $"{e.ErrorType} at {e.Path?.XPath}: {e.Description}")));
    }
}
