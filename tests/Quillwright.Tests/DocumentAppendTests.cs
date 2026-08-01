using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// Appending one document to another: the content is copied with everything it leans on —
/// styles, numbering, images, notes, comments, bookmarks — remapped so the result saves valid
/// and the two documents stay independent.
/// </summary>
public class DocumentAppendTests
{
    [Fact]
    public async Task Content_ArrivesAfterTheTargetsOwn()
    {
        WordDocument target = WordDocument.Create();
        target.Sections[0].AddParagraph("Chapter one.");

        WordDocument source = WordDocument.Create();
        source.Sections[0].AddParagraph("Chapter two.", "Heading1");
        source.Sections[0].AddParagraph("Its body.");

        IReadOnlyList<DocumentWarning> warnings = target.Append(source);

        Assert.Empty(warnings);
        WordDocument reloaded = await DocumentFixture.RoundTripAsync(target, "an appended document");
        Assert.Equal(
            ["Chapter one.", "Chapter two.", "Its body."],
            reloaded.Paragraphs.Select(static p => p.Text).ToArray());
        Assert.Equal("Heading1", reloaded.Paragraphs.ElementAt(1).Format.StyleId);
    }

    [Fact]
    public void TheSource_IsNotChanged()
    {
        WordDocument target = WordDocument.Create();
        WordDocument source = WordDocument.Create();
        Paragraph original = source.Sections[0].AddParagraph("untouched");
        int blocks = source.Sections[0].Blocks.Count;

        target.Append(source);
        target.Paragraphs.First().AppendText(" and edited");

        Assert.Equal("untouched", original.Text);
        Assert.Equal(blocks, source.Sections[0].Blocks.Count);
    }

    [Fact]
    public void AStyleTheTargetLacks_ComesAlongWithItsChain()
    {
        WordDocument source = WordDocument.Create();
        Style baseStyle = source.Styles.GetOrAdd("HouseBase");
        baseStyle.RunFormat = baseStyle.RunFormat with { Bold = true };
        Style derived = source.Styles.GetOrAdd("HouseBody");
        derived.BasedOn = "HouseBase";
        Paragraph paragraph = source.Sections[0].AddParagraph("styled");
        paragraph.Format = paragraph.Format with { StyleId = "HouseBody" };

        WordDocument target = WordDocument.Create();
        target.Append(source);

        Assert.NotNull(target.Styles.Find("HouseBody"));
        Assert.NotNull(target.Styles.Find("HouseBase"));
        Assert.Equal("HouseBase", target.Styles.Find("HouseBody")!.BasedOn);
    }

    [Fact]
    public void AStyleTheTargetAlreadyHas_Wins()
    {
        WordDocument target = WordDocument.Create();
        Style hosts = target.Styles.GetOrAdd("Disputed");
        hosts.RunFormat = hosts.RunFormat with { Bold = true };

        WordDocument source = WordDocument.Create();
        Style sources = source.Styles.GetOrAdd("Disputed");
        sources.RunFormat = sources.RunFormat with { Italic = true };
        Paragraph paragraph = source.Sections[0].AddParagraph("who wins");
        paragraph.Format = paragraph.Format with { StyleId = "Disputed" };

        target.Append(source);

        Assert.True(target.Styles.Find("Disputed")!.RunFormat.Bold);
        Assert.NotEqual(true, target.Styles.Find("Disputed")!.RunFormat.Italic);
    }

    [Fact]
    public async Task Lists_KeepCountingSeparately()
    {
        WordDocument target = WordDocument.Create();
        int targetList = target.Numbering.AddNumberedList();
        Paragraph one = target.Sections[0].AddParagraph("target item");
        one.Format = one.Format with { NumberingId = targetList, NumberingLevel = 0 };

        WordDocument source = WordDocument.Create();
        int sourceList = source.Numbering.AddNumberedList();
        Paragraph two = source.Sections[0].AddParagraph("source item");
        two.Format = two.Format with { NumberingId = sourceList, NumberingLevel = 0 };

        target.Append(source);
        WordDocument reloaded = await DocumentFixture.RoundTripAsync(target, "appended numbering");

        List<Paragraph> items = [.. reloaded.Paragraphs];
        Assert.NotNull(items[0].Format.NumberingId);
        Assert.NotNull(items[1].Format.NumberingId);
        Assert.NotEqual(items[0].Format.NumberingId, items[1].Format.NumberingId);
    }

    [Fact]
    public async Task PicturesTravel_AndTheImageLandsInTheTargetsMedia()
    {
        WordDocument source = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendPicture(Png());
        source.Sections[0].Blocks.Add(paragraph);

        WordDocument target = WordDocument.Create();
        target.Sections[0].AddParagraph("before");
        target.Append(source);

        Assert.Single(target.Media);

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(target, "an appended picture");
        Picture picture = reloaded.Paragraphs
            .SelectMany(static p => p.Objects)
            .Select(static o => o.Object)
            .OfType<Picture>()
            .Single();
        Assert.Equal("image/png", picture.Image.ContentType);
    }

    [Fact]
    public async Task FootnotesArrive_WithFreshIds()
    {
        WordDocument source = WordDocument.Create();
        Paragraph noted = source.Sections[0].AddParagraph("Noted.");
        Note note = source.AddFootnote(noted, "The footnote's words.");
        Assert.NotEqual(0, note.Id);

        WordDocument target = WordDocument.Create();
        Paragraph hostNoted = target.Sections[0].AddParagraph("Host note.");
        target.AddFootnote(hostNoted, "Existing footnote.");

        target.Append(source);
        WordDocument reloaded = await DocumentFixture.RoundTripAsync(target, "appended footnotes");

        Assert.Contains(reloaded.Footnotes, static n => n.GetText().Contains("The footnote's words.", StringComparison.Ordinal));
        Assert.Contains(reloaded.Footnotes, static n => n.GetText().Contains("Existing footnote.", StringComparison.Ordinal));

        List<int> ids = [.. reloaded.Footnotes.Where(static n => n.Kind == NoteKind.Normal).Select(static n => n.Id)];
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task CommentsArrive_WithTheirThreading()
    {
        WordDocument source = WordDocument.Create();
        Paragraph discussed = source.Sections[0].AddParagraph("Debated wording.");
        Comment comment = source.AddComment(discussed, 0, 7, "Too strong?", "Ada");
        source.AddReply(comment, "Agreed.", "Grace");

        WordDocument target = WordDocument.Create();
        target.Sections[0].AddParagraph("Preamble.");
        target.Append(source);

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(target, "appended comments");

        Assert.Equal(2, reloaded.Comments.Count);
        Comment reply = reloaded.Comments.Single(static c => c.ParentId is not null);
        Comment parent = reloaded.Comments.Single(static c => c.ParentId is null);
        Assert.Equal(parent.Id, reply.ParentId);
        Assert.Equal("Ada", parent.Author);
    }

    [Fact]
    public async Task BookmarksShift_ClearOfTheTargets()
    {
        WordDocument target = WordDocument.Create();
        Paragraph hostMarked = target.Sections[0].AddParagraph("host");
        hostMarked.AddMark(new BookmarkStart { Id = 3, Name = "host" }, 0);
        hostMarked.AddMark(new BookmarkEnd { Id = 3 }, hostMarked.TextLength);

        WordDocument source = WordDocument.Create();
        Paragraph marked = source.Sections[0].AddParagraph("appended");
        marked.AddMark(new BookmarkStart { Id = 3, Name = "appended" }, 0);
        marked.AddMark(new BookmarkEnd { Id = 3 }, marked.TextLength);

        target.Append(source);
        WordDocument reloaded = await DocumentFixture.RoundTripAsync(target, "appended bookmarks");

        List<int> ids = [.. reloaded.Paragraphs
            .SelectMany(static p => p.Marks)
            .Select(static m => m.Mark)
            .OfType<BookmarkStart>()
            .Select(static b => b.Id)];
        Assert.Equal(2, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void HyperlinksRebind_ByUrlRatherThanByRelationship()
    {
        WordDocument source = WordDocument.Create();
        Paragraph linked = source.Sections[0].AddParagraph("visit the site");
        linked.AddRange(new Hyperlink { Url = "https://example.org" }, 0, 5);

        WordDocument target = WordDocument.Create();
        target.Append(source);

        Hyperlink copied = target.Paragraphs.First().Ranges.Select(static r => r.Range).OfType<Hyperlink>().Single();
        Assert.Equal("https://example.org", copied.Url);
        Assert.Null(copied.RelationshipId);
    }

    [Fact]
    public async Task KeepSections_CarriesPageSetupAndHeaders()
    {
        WordDocument source = WordDocument.Create();
        source.Sections[0].Properties.PageWidth = Length.FromCentimeters(20);
        HeaderFooter header = source.Sections[0].Headers.GetOrCreate();
        header.AddParagraph("Appendix header");
        source.Sections[0].AddParagraph("Appendix body.");

        WordDocument target = WordDocument.Create();
        target.Sections[0].AddParagraph("Main body.");

        target.Append(source, new DocumentAppendOptions { KeepSections = true });
        WordDocument reloaded = await DocumentFixture.RoundTripAsync(target, "appended sections");

        Assert.Equal(2, reloaded.Sections.Count);
        Assert.Equal(Length.FromCentimeters(20).Twips, reloaded.Sections[1].Properties.PageWidth.Twips);
        Assert.Contains("Appendix header", reloaded.Sections[1].Headers.Default?.GetText() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void StartOnNewPage_BreaksBeforeTheAppendedContent()
    {
        WordDocument source = WordDocument.Create();
        source.Sections[0].AddParagraph("Second chapter.");

        WordDocument target = WordDocument.Create();
        target.Sections[0].AddParagraph("First chapter.");

        target.Append(source, new DocumentAppendOptions { StartOnNewPage = true });

        Assert.True(target.Paragraphs.ElementAt(1).Format.PageBreakBefore);
    }

    [Fact]
    public void AppendingToItself_IsRefused()
    {
        WordDocument document = WordDocument.Create();
        Assert.Throws<ArgumentException>(() => document.Append(document));
    }

    [Fact]
    public async Task ATableWithItsStyle_Arrives()
    {
        WordDocument source = WordDocument.Create();
        var table = new Table();
        table.Format = table.Format with { StyleId = source.Styles.GetOrAdd("TableGrid", StyleKind.Table).Id };
        var row = new TableRow();
        row.AddCell("cell one");
        row.AddCell("cell two");
        table.Rows.Add(row);
        source.Sections[0].Blocks.Add(table);

        WordDocument target = WordDocument.Create();
        target.Append(source);

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(target, "an appended table");
        Table arrived = reloaded.Sections[0].Blocks.OfType<Table>().Single();
        Assert.Equal("TableGrid", arrived.Format.StyleId);
        Assert.NotNull(reloaded.Styles.Find("TableGrid"));
        Assert.Equal("cell two", arrived.Rows[0].Cells[1].GetText());
    }

    private static ImageData Png()
    {
        // The smallest valid PNG: a 1x1 transparent pixel.
        const string Base64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        return ImageData.FromBytes(Convert.FromBase64String(Base64));
    }
}
