using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// Equations are written in a vocabulary of their own that the model deliberately does not
/// interpret. The contract is therefore preservation: an equation has to come out of a round
/// trip exactly as it went in, and has to stay in the right place in the text.
/// </summary>
public class OfficeMathTests
{
    private const string Namespace = " xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";

    private const string Fraction =
        "<m:oMath><m:r><m:t>x=</m:t></m:r><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num>" +
        "<m:den><m:r><m:t>2</m:t></m:r></m:den></m:f></m:oMath>";

    [Fact]
    public async Task AnInlineEquation_IsPreservedVerbatim()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("before ");
        paragraph.AppendObject(new RawInline(Fraction, isRunChild: false));
        paragraph.AppendText(" after");
        document.Sections[0].Blocks.Add(paragraph);

        string xml = await MarkupAsync(document);

        Assert.Contains(Fraction, xml, StringComparison.Ordinal);
        Assert.Contains(Namespace, xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEquation_KeepsItsPlaceBetweenTheRunsAroundIt()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("before ");
        paragraph.AppendObject(new RawInline(Fraction, isRunChild: false));
        paragraph.AppendText(" after");
        document.Sections[0].Blocks.Add(paragraph);

        string xml = await MarkupAsync(document);
        int before = xml.IndexOf("before", StringComparison.Ordinal);
        int equation = xml.IndexOf("<m:oMath>", StringComparison.Ordinal);
        int after = xml.IndexOf("after", StringComparison.Ordinal);

        Assert.True(before < equation && equation < after, $"positions were {before}, {equation}, {after}");
    }

    [Fact]
    public async Task AnEquationSurvivesRepeatedRoundTrips()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendObject(new RawInline(Fraction, isRunChild: false));
        document.Sections[0].Blocks.Add(paragraph);

        // Reading a fragment out of a part makes the namespace it inherited explicit on the
        // fragment itself, so the second pass is not byte-identical to the first. What must
        // hold is that it settles: the third pass matches the second.
        string twice = await MarkupAsync(await ReloadAsync(document));
        string thrice = await MarkupAsync(await ReloadAsync(await ReloadAsync(document)));

        Assert.Contains("<m:oMath" + Namespace + ">", twice, StringComparison.Ordinal);
        Assert.Contains("<m:t>x=</m:t>", twice, StringComparison.Ordinal);
        Assert.Equal(Body(twice), Body(thrice));
    }

    private static string Body(string xml) => xml[xml.IndexOf("<w:body>", StringComparison.Ordinal)..];

    [Fact]
    public async Task ARunMarkedAsPartOfAnEquation_KeepsTheFlag()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendText("y", RunFormat.Default with { OfficeMath = true });
        document.Sections[0].Blocks.Add(paragraph);

        WordDocument reopened = await ReloadAsync(document);
        Paragraph result = reopened.Sections[0].Blocks.OfType<Paragraph>().First();

        Assert.True(result.Runs.First().Format.OfficeMath);
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
