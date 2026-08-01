using Quillwright.Diagnostics;
using Quillwright.Doc.Writing;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// The binary format has no equations. What it can keep is the text inside one, so the tests
/// here check that a formula leaves a readable trace rather than a hole, and that the loss is
/// reported rather than silent.
/// </summary>
public class DocMathTests
{
    private const string Fraction =
        "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">" +
        "<m:r><m:t>x=</m:t></m:r><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num>" +
        "<m:den><m:r><m:t>2</m:t></m:r></m:den></m:f></m:oMath>";

    [Fact]
    public void AnEquationsText_IsRecovered() =>
        Assert.Equal("x=12", OfficeMathText.Extract(Fraction));

    [Fact]
    public void MarkupThatIsNotAnEquation_IsLeftAlone() =>
        Assert.Null(OfficeMathText.Extract("<w:something><w:t>text</w:t></w:something>"));

    [Fact]
    public void AnInlineEquation_BecomesTextInTheDocument()
    {
        var warnings = new List<DocumentWarning>();
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("before ");
        paragraph.AppendObject(new RawInline(Fraction));
        paragraph.AppendText(" after");
        document.Sections[0].Blocks.Add(paragraph);

        WordDocument reopened = DocReader.Load(DocWriter.Save(document, new DocWriteOptions { OnWarning = warnings.Add }));

        Assert.Equal("before x=12 after", First(reopened).Text);
        Assert.Contains(warnings, static w => w.Message.Contains("equation", StringComparison.Ordinal));
    }

    [Fact]
    public void ADisplayEquation_BecomesAParagraphOfItsOwn()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("before"));
        document.Sections[0].Blocks.Add(new RawBlock(
            "<m:oMathPara xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">" +
            "<m:oMath><m:r><m:t>E=mc</m:t></m:r></m:oMath></m:oMathPara>"));
        document.Sections[0].Blocks.Add(new Paragraph("after"));

        List<string> paragraphs =
        [
            .. DocReader.Load(DocWriter.Save(document))
                .Sections.SelectMany(static s => s.Blocks)
                .OfType<Paragraph>()
                .Select(static p => p.Text),
        ];

        Assert.Equal(["before", "E=mc", "after"], paragraphs);
    }

    [Fact]
    public void PreservedMarkupThatIsNotAnEquation_IsDroppedWithAWarning()
    {
        var warnings = new List<DocumentWarning>();
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("kept"));
        document.Sections[0].Blocks.Add(new RawBlock("<w:altChunk r:id=\"rId9\"/>"));

        WordDocument reopened = DocReader.Load(DocWriter.Save(document, new DocWriteOptions { OnWarning = warnings.Add }));

        Assert.Equal("kept", First(reopened).Text);
        Assert.Contains(warnings, static w => w.Code == WarningCode.PreservedVerbatim);
    }

    private static Paragraph First(WordDocument document) =>
        document.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();
}
