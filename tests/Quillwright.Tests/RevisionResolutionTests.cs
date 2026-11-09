using System.IO.Compression;
using System.Text;
using Quillwright.Diagnostics;
using Quillwright.Editing;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Tests;

/// <summary>
/// Tracked changes that are not text: formatting, paragraph properties, and paragraph marks whose
/// removal has to bring everything in the next paragraph along.
/// </summary>
public sealed class RevisionResolutionTests
{
    private const string PropertyRecord =
        "<w:pPrChange w:id=\"7\" w:author=\"A\"><w:pPr><w:jc w:val=\"center\"/></w:pPr></w:pPrChange>";

    private static RunFormat Bold(RunFormat format) => format with { Bold = true };

    private static RunFormat Italic(RunFormat format) => format with { Italic = true };

    private static (WordDocument Document, Paragraph Paragraph) OneParagraph(string text = "Hello world")
    {
        WordDocument document = WordDocument.Create();
        return (document, document.Sections[0].AddParagraph(text));
    }

    private static async Task<string> DocumentXmlAsync(WordDocument document)
    {
        using MemoryStream buffer = await DocumentFixture.SaveAsync(document);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ATrackedFormatChangeIsARevisionThatCanBeRejected()
    {
        (WordDocument document, Paragraph paragraph) = OneParagraph();
        using (document.TrackChanges("A"))
            paragraph.ApplyFormat(0, 5, Bold);

        Assert.True(document.HasRevisions());
        Assert.Contains("rPrChange", await DocumentXmlAsync(document), StringComparison.Ordinal);

        Assert.Equal(1, document.RejectAllRevisions());

        Assert.False(document.HasRevisions());
        Assert.Null(paragraph.FormatAt(0).Bold);
        Assert.Null(paragraph.FormatAt(0).ChangeXml);
        Assert.DoesNotContain("rPrChange", await DocumentXmlAsync(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptingAFormatChangeKeepsTheFormatAndDropsTheRecord()
    {
        (WordDocument document, Paragraph paragraph) = OneParagraph();
        using (document.TrackChanges("A"))
            paragraph.ApplyFormat(0, 5, Bold);

        Assert.Equal(1, document.AcceptAllRevisions());

        Assert.False(document.HasRevisions());
        Assert.True(paragraph.FormatAt(0).Bold);
        Assert.DoesNotContain("rPrChange", await DocumentXmlAsync(document), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWholeParagraphOverloadIsRecordedToo()
    {
        (WordDocument document, Paragraph paragraph) = OneParagraph();
        using (document.TrackChanges("A"))
            paragraph.ApplyFormat(Bold);

        Assert.NotNull(paragraph.FormatAt(0).ChangeXml);
        Assert.NotNull(paragraph.MarkFormat.ChangeXml);

        Assert.Equal(2, document.RejectAllRevisions());

        Assert.Null(paragraph.FormatAt(0).Bold);
        Assert.Null(paragraph.MarkFormat.Bold);
        Assert.False(document.HasRevisions());
    }

    [Fact]
    public void ARunChangedTwiceRejectsToItsOriginalLook()
    {
        (WordDocument document, Paragraph paragraph) = OneParagraph();
        using (document.TrackChanges("A"))
        {
            paragraph.ApplyFormat(0, 5, Bold);
            paragraph.ApplyFormat(0, 5, Italic);
        }

        document.RejectAllRevisions();

        Assert.Null(paragraph.FormatAt(0).Bold);
        Assert.Null(paragraph.FormatAt(0).Italic);
    }

    [Fact]
    public void RejectingRestoresFormattingTheRunAlreadyHad()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("Hello", new RunFormat { Italic = true });

        using (document.TrackChanges("A"))
            paragraph.ApplyFormat(0, 5, Bold);

        document.RejectAllRevisions();

        Assert.True(paragraph.FormatAt(0).Italic);
        Assert.Null(paragraph.FormatAt(0).Bold);
    }

    [Fact]
    public async Task AFormatChangeReadFromAFileCanBeRejected()
    {
        (WordDocument document, Paragraph paragraph) = OneParagraph();
        using (document.TrackChanges("A"))
            paragraph.ApplyFormat(0, 5, Bold);

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "tracked format");

        Assert.True(reloaded.HasRevisions());
        Assert.Equal(1, reloaded.RejectAllRevisions());

        Paragraph restored = reloaded.Sections[0].Blocks.Paragraphs.First();
        Assert.Null(restored.FormatAt(0).Bold);
        Assert.False(reloaded.HasRevisions());
    }

    [Fact]
    public void AnUnchangedFormatIsNotRecorded()
    {
        (WordDocument document, Paragraph paragraph) = OneParagraph();
        using (document.TrackChanges("A"))
            paragraph.ApplyFormat(0, 5, static format => format);

        Assert.False(document.HasRevisions());
    }

    [Fact]
    public void AParagraphPropertyChangeCanBeRejected()
    {
        (WordDocument document, Paragraph paragraph) = OneParagraph();
        paragraph.Format = paragraph.Format with { Alignment = ParagraphAlignment.Right, ChangeXml = PropertyRecord };

        Assert.True(document.HasRevisions());
        Assert.Equal(1, document.RejectAllRevisions());

        Assert.Equal(ParagraphAlignment.Center, paragraph.Format.Alignment);
        Assert.Null(paragraph.Format.ChangeXml);
        Assert.False(document.HasRevisions());
    }

    [Fact]
    public void AParagraphPropertyChangeCanBeAccepted()
    {
        (WordDocument document, Paragraph paragraph) = OneParagraph();
        paragraph.Format = paragraph.Format with { Alignment = ParagraphAlignment.Right, ChangeXml = PropertyRecord };

        Assert.Equal(1, document.AcceptAllRevisions());

        Assert.Equal(ParagraphAlignment.Right, paragraph.Format.Alignment);
        Assert.Null(paragraph.Format.ChangeXml);
    }

    private static (WordDocument Document, Table Table) OneTable()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        document.Sections[0].Blocks.Add(table);
        var row = new TableRow();
        table.Rows.Add(row);
        row.Cells.Add(new TableCell());
        return (document, table);
    }

    [Fact]
    public void TablePropertyChangesAreDetectedAndCanBeAccepted()
    {
        (WordDocument document, Table table) = OneTable();
        table.Format = table.Format with { ChangeXml = "<w:tblPrChange w:id=\"1\" w:author=\"A\"><w:tblPr/></w:tblPrChange>" };
        table.Rows[0].Format = table.Rows[0].Format with { ChangeXml = "<w:trPrChange w:id=\"2\" w:author=\"A\"><w:trPr/></w:trPrChange>" };

        Assert.True(document.HasRevisions());
        Assert.Equal(2, document.AcceptAllRevisions());
        Assert.False(document.HasRevisions());
    }

    [Fact]
    public void RejectingATablePropertyChangeLeavesItReported()
    {
        (WordDocument document, Table table) = OneTable();
        table.Format = table.Format with { ChangeXml = "<w:tblPrChange w:id=\"1\" w:author=\"A\"><w:tblPr/></w:tblPrChange>" };

        Assert.Equal(0, document.RejectAllRevisions());
        Assert.True(document.HasRevisions());
    }

    [Fact]
    public void ASectionPropertyChangeIsDetectedAndCanBeAccepted()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("x");
        document.Sections[0].Properties.ChangeXml = "<w:sectPrChange w:id=\"1\" w:author=\"A\"><w:sectPr/></w:sectPrChange>";

        Assert.True(document.HasRevisions());
        Assert.Equal(1, document.AcceptAllRevisions());
        Assert.False(document.HasRevisions());
    }

    /// <summary>A paragraph deleted under tracking, followed by one holding every kind of content.</summary>
    private static (WordDocument Document, Paragraph Deleted, Paragraph Rich, ImageData Image) MergeCase()
    {
        WordDocument document = WordDocument.Create();
        Paragraph deleted = document.Sections[0].AddParagraph("gone");

        Paragraph rich = document.Sections[0].AddParagraph();
        rich.Format = rich.Format with { Alignment = ParagraphAlignment.Right };
        rich.MarkFormat = rich.MarkFormat with { Italic = true };
        rich.AppendText("plain ");
        rich.AppendText("bold", new RunFormat { Bold = true });
        rich.AddRange(new Hyperlink { Url = "https://example.org" }, 0, 5);
        rich.AddMark(new BookmarkStart { Id = 3, Name = "kept" }, 0);
        rich.AddMark(new BookmarkEnd { Id = 3 }, rich.TextLength);
        ImageData image = ImageData.FromBytes(TestImages.Png);
        rich.AppendPicture(image, Length.FromCentimeters(1), Length.FromCentimeters(1));

        return (document, deleted, rich, image);
    }

    private static void AssertMerged(WordDocument document, ImageData image)
    {
        Paragraph merged = Assert.Single(document.Sections[0].Blocks.Paragraphs);
        Assert.StartsWith("plain bold", merged.Text, StringComparison.Ordinal);
        Assert.True(merged.FormatAt(6).Bold);
        Assert.Null(merged.FormatAt(0).Bold);
        Assert.Equal(ParagraphAlignment.Right, merged.Format.Alignment);
        Assert.True(merged.MarkFormat.Italic);

        (int start, int length, InlineRange range) = Assert.Single(merged.Ranges, static entry => entry.Range is Hyperlink);
        Assert.Equal(0, start);
        Assert.Equal(5, length);
        Assert.Equal("https://example.org", ((Hyperlink)range).Url);

        Assert.Contains(merged.Marks, static mark => mark.Mark is BookmarkStart { Name: "kept" });
        Assert.Contains(merged.Marks, static mark => mark.Mark is BookmarkEnd);

        (int offset, InlineObject inline) = Assert.Single(merged.Objects, static entry => entry.Object is Picture);
        Assert.Equal(merged.TextLength - 1, offset);
        Assert.True(image.Bytes.Span.SequenceEqual(((Picture)inline).Image.Bytes.Span));
    }

    [Fact]
    public async Task AcceptingADeletedMarkKeepsEverythingInTheNextParagraph()
    {
        (WordDocument document, Paragraph deleted, _, ImageData image) = MergeCase();
        using (document.TrackChanges("A"))
            deleted.Delete();

        document.AcceptAllRevisions();

        AssertMerged(document, image);
        Assert.False(document.HasRevisions());
        AssertMerged(await DocumentFixture.RoundTripAsync(document, "merged paragraph"), image);
    }

    [Fact]
    public void RejectingADeletedMarkLeavesBothParagraphs()
    {
        (WordDocument document, Paragraph deleted, Paragraph rich, _) = MergeCase();
        using (document.TrackChanges("A"))
            deleted.Delete();

        document.RejectAllRevisions();

        Assert.Equal(2, document.Sections[0].Blocks.Paragraphs.Count());
        Assert.Equal("gone", deleted.Text);
        Assert.Single(rich.Objects);
    }

    [Fact]
    public void ChainedDeletedMarksMergeIntoTheLastParagraph()
    {
        WordDocument document = WordDocument.Create();
        Paragraph first = document.Sections[0].AddParagraph("one");
        Paragraph second = document.Sections[0].AddParagraph("two");
        Paragraph third = document.Sections[0].AddParagraph("three");
        third.AppendText(" more", new RunFormat { Bold = true });

        using (document.TrackChanges("A"))
        {
            first.Delete();
            second.Delete();
        }

        document.AcceptAllRevisions();

        Paragraph merged = Assert.Single(document.Sections[0].Blocks.Paragraphs);
        Assert.Equal("three more", merged.Text);
        Assert.True(merged.FormatAt(6).Bold);
    }

    // Header and footer parts ---------------------------------------------------------------------

    private static byte[] Rewrite(byte[] package, Action<ZipArchive> change)
    {
        using var buffer = new MemoryStream();
        buffer.Write(package);
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
            change(archive);

        return buffer.ToArray();
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void ReplaceEntry(ZipArchive archive, string name, string content)
    {
        archive.GetEntry(name)!.Delete();
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void RenamePart(ZipArchive archive, string from, string to)
    {
        string content = ReadEntry(archive, from);
        archive.GetEntry(from)!.Delete();
        using (var writer = new StreamWriter(archive.CreateEntry(to).Open(), new UTF8Encoding(false)))
            writer.Write(content);

        string relationships = ReadEntry(archive, "word/_rels/document.xml.rels");
        ReplaceEntry(archive, "word/_rels/document.xml.rels", relationships.Replace(
            Path.GetFileName(from), to["word/".Length..], StringComparison.Ordinal));

        string types = ReadEntry(archive, "[Content_Types].xml");
        ReplaceEntry(archive, "[Content_Types].xml", types.Replace("/" + from, "/" + to, StringComparison.Ordinal));
    }

    private static async Task<byte[]> HeaderAndFooterPackageAsync()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        document.Sections[0].Headers.GetOrCreate().AddParagraph("TOP");
        document.Sections[0].Footers.GetOrCreate().AddParagraph("BOTTOM");

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        return saved.ToArray();
    }

    [Fact]
    public async Task AFooterInAPartWithAnyNameLoadsAsAFooter()
    {
        byte[] package = Rewrite(await HeaderAndFooterPackageAsync(), archive =>
            RenamePart(archive, "word/footer1.xml", "word/ftr-a.xml"));

        WordDocument document = await WordDocument.LoadAsync(new MemoryStream(package), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("BOTTOM", document.Sections[0].Footers.Default?.GetText());

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "renamed footer");
        Assert.Equal("BOTTOM", reloaded.Sections[0].Footers.Default?.GetText());
        Assert.Equal("TOP", reloaded.Sections[0].Headers.Default?.GetText());
    }

    [Fact]
    public async Task AHeaderWhosePartNameMentionsFooterStaysAHeader()
    {
        byte[] package = Rewrite(await HeaderAndFooterPackageAsync(), archive =>
            RenamePart(archive, "word/header1.xml", "word/footer-style-header.xml"));

        WordDocument document = await WordDocument.LoadAsync(new MemoryStream(package), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("TOP", document.Sections[0].Headers.Default?.GetText());
        Assert.False(document.Sections[0].Headers.Default?.IsFooter);
    }

    [Fact]
    public async Task ARootThatDisagreesWithItsRelationshipWarnsButKeepsTheText()
    {
        byte[] package = Rewrite(await HeaderAndFooterPackageAsync(), archive =>
        {
            string footer = ReadEntry(archive, "word/footer1.xml");
            ReplaceEntry(archive, "word/footer1.xml", footer.Replace("w:ftr", "w:hdr", StringComparison.Ordinal));
        });

        var warnings = new List<DocumentWarning>();
        WordDocument document = await WordDocument.LoadAsync(
            new MemoryStream(package),
            new LoadOptions { OnWarning = warnings.Add },
            TestContext.Current.CancellationToken);

        Assert.Equal("BOTTOM", document.Sections[0].Footers.Default?.GetText());
        Assert.Contains(warnings, static warning => warning.Code == WarningCode.UnexpectedElement);
    }

    // Optional property parts ----------------------------------------------------------------------

    private static (bool Declared, bool Present) RootReference(byte[] package, string partName)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return (ReadEntry(archive, "_rels/.rels").Contains(partName, StringComparison.Ordinal), archive.GetEntry(partName) is not null);
    }

    [Fact]
    public async Task ClearingCustomPropertiesAlsoRemovesTheirRelationship()
    {
        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("x");
        original.CustomProperties.Set("Project", "Apollo");
        using MemoryStream saved = await DocumentFixture.SaveAsync(original);
        Assert.Equal((true, true), RootReference(saved.ToArray(), "docProps/custom.xml"));

        WordDocument document = await WordDocument.LoadAsync(new MemoryStream(saved.ToArray()), cancellationToken: TestContext.Current.CancellationToken);
        document.CustomProperties.Clear();
        using MemoryStream cleared = await DocumentFixture.SaveAsync(document);

        Assert.Equal((false, false), RootReference(cleared.ToArray(), "docProps/custom.xml"));
        await DocumentFixture.RoundTripAsync(document, "cleared custom properties");
    }

    [Fact]
    public async Task RemovingTheLastCustomPropertyAlsoRemovesTheRelationship()
    {
        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("x");
        original.CustomProperties.Set("Project", "Apollo");
        using MemoryStream saved = await DocumentFixture.SaveAsync(original);

        WordDocument document = await WordDocument.LoadAsync(new MemoryStream(saved.ToArray()), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(document.CustomProperties.Remove("Project"));
        using MemoryStream cleared = await DocumentFixture.SaveAsync(document);

        Assert.Equal((false, false), RootReference(cleared.ToArray(), "docProps/custom.xml"));
    }

    [Fact]
    public async Task KeepingCustomPropertiesKeepsTheirRelationship()
    {
        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("x");
        original.CustomProperties.Set("Project", "Apollo");
        using MemoryStream saved = await DocumentFixture.SaveAsync(original);

        WordDocument document = await WordDocument.LoadAsync(new MemoryStream(saved.ToArray()), cancellationToken: TestContext.Current.CancellationToken);
        using MemoryStream resaved = await DocumentFixture.SaveAsync(document);

        Assert.Equal((true, true), RootReference(resaved.ToArray(), "docProps/custom.xml"));
    }

    [Fact]
    public async Task AnEmptyApplicationPropertiesPartLeavesNoDanglingRelationship()
    {
        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("x");
        original.ApplicationProperties.Company = "Acme";
        using MemoryStream saved = await DocumentFixture.SaveAsync(original);

        byte[] package = Rewrite(saved.ToArray(), archive =>
            ReplaceEntry(archive, "docProps/app.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"/>"));

        WordDocument document = await WordDocument.LoadAsync(new MemoryStream(package), cancellationToken: TestContext.Current.CancellationToken);
        using MemoryStream resaved = await DocumentFixture.SaveAsync(document);

        (bool declared, bool present) = RootReference(resaved.ToArray(), "docProps/app.xml");
        Assert.Equal(present, declared);
    }
}
