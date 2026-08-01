using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests;

public class ParagraphIndentTests
{
    [Fact]
    public async Task CharacterUnitIndents_SurviveAValidatedRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Paragraph first = document.Sections[0].AddParagraph("first line");
        first.Format = first.Format with
        {
            IndentLeft = Length.FromTwips(720),
            IndentRight = Length.FromTwips(360),
            IndentFirstLine = Length.FromTwips(240),
            IndentLeftCharacters = 250,
            IndentRightCharacters = 150,
            IndentFirstLineCharacters = 140,
        };
        Paragraph hanging = document.Sections[0].AddParagraph("hanging");
        hanging.Format = hanging.Format with
        {
            IndentHanging = Length.FromTwips(240),
            IndentHangingCharacters = 100,
        };

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        OpenXmlAssert.Valid(saved, "character-unit paragraph indents");
        string markup = OpenXmlAssert.ReadPart(saved, "word/document.xml");
        Assert.Contains("w:leftChars=\"250\"", markup, StringComparison.Ordinal);
        Assert.Contains("w:rightChars=\"150\"", markup, StringComparison.Ordinal);
        Assert.Contains("w:firstLineChars=\"140\"", markup, StringComparison.Ordinal);
        Assert.Contains("w:hangingChars=\"100\"", markup, StringComparison.Ordinal);

        saved.Position = 0;
        WordDocument reopened = await WordDocument.LoadAsync(
            saved, cancellationToken: TestContext.Current.CancellationToken);
        ParagraphFormat firstFormat = reopened.Paragraphs.First().Format;
        ParagraphFormat hangingFormat = reopened.Paragraphs.Last().Format;

        Assert.Equal(250, firstFormat.IndentLeftCharacters);
        Assert.Equal(150, firstFormat.IndentRightCharacters);
        Assert.Equal(140, firstFormat.IndentFirstLineCharacters);
        Assert.Equal(100, hangingFormat.IndentHangingCharacters);
    }

    [Fact]
    public async Task StartAndEndCharacterSpellings_AreRead()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("strict names");
        paragraph.Format = paragraph.Format with { IndentLeftCharacters = 225, IndentRightCharacters = 175 };
        using MemoryStream plain = await DocumentFixture.SaveAsync(document);
        using MemoryStream renamed = SignedPackage.Rewrite(plain, "word/document.xml", static xml => xml
            .Replace("w:leftChars=", "w:startChars=", StringComparison.Ordinal)
            .Replace("w:rightChars=", "w:endChars=", StringComparison.Ordinal));

        WordDocument reopened = await WordDocument.LoadAsync(
            renamed, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(225, reopened.Paragraphs.Single().Format.IndentLeftCharacters);
        Assert.Equal(175, reopened.Paragraphs.Single().Format.IndentRightCharacters);
    }

    [Fact]
    public async Task CharacterIndentsInTheReferenceCorpus_DoNotDisappear()
    {
        string fixture = ReferenceCorpus.RequireOpenXmlPath(
            "test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/wordprocessing/ParaPr/paraind.docx");
        WordDocument document = await WordDocument.LoadAsync(
            fixture, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(document.Paragraphs,
            static paragraph => paragraph.Format is { IndentLeftCharacters: 250, IndentRightCharacters: 150 });

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        saved.Position = 0;
        WordDocument reopened = await WordDocument.LoadAsync(
            saved, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(reopened.Paragraphs,
            static paragraph => paragraph.Format is { IndentLeftCharacters: 250, IndentRightCharacters: 150 });
    }
}
