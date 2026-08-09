using System.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Rtf.Tests;

public class RtfWriterTests
{
    [Fact]
    public void CommentsAndReplies_RoundTripWithAnchorsAuthorsDatesAndBodies()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment comment = document.AddComment(paragraph, 0, 8, "First review note.", "Ada Lovelace", "AL");
        comment.AddParagraph("Second comment paragraph.");
        comment.Date = new DateTimeOffset(2024, 5, 6, 14, 37, 0, TimeSpan.Zero);
        Comment reply = document.AddReply(comment, "A reply.", "Grace Hopper", "GH");
        reply.Date = new DateTimeOffset(2024, 5, 6, 15, 2, 0, TimeSpan.Zero);

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());
        Assert.Contains(@"\atrfstart 1", exported.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"\atrfend 1", exported.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"\atnid AL", exported.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"\atnauthor Ada Lovelace", exported.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"\atndate ", exported.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"\atnparent -1", exported.ToString(), StringComparison.Ordinal);

        Paragraph reopened = imported.Document.Sections[0].Blocks.Paragraphs.Single();
        Assert.Equal("Reviewed text.", reopened.GetText());
        Assert.Equal(2, imported.Document.Comments.Count);
        Comment first = imported.Document.Comments.Single(static value => value.ParentId is null);
        Comment second = imported.Document.Comments.Single(static value => value.ParentId is not null);
        Assert.Equal("First review note.\nSecond comment paragraph.", first.GetText());
        Assert.Equal(2, first.Blocks.Paragraphs.Count());
        Assert.Equal("Ada Lovelace", first.Author);
        Assert.Equal("AL", first.Initials);
        Assert.Equal(new DateTimeOffset(2024, 5, 6, 14, 37, 0, TimeSpan.Zero), first.Date);
        Assert.Equal("A reply.", second.GetText());
        Assert.Equal("Grace Hopper", second.Author);
        Assert.Equal("GH", second.Initials);
        Assert.Equal(first.Id, second.ParentId);
        Assert.Equal(2, reopened.Marks.Count(static item => item.Mark is CommentRangeStart));
        Assert.Equal(2, reopened.Marks.Count(static item => item.Mark is CommentRangeEnd));
        Assert.Equal(2, reopened.Objects.Count(static item => item.Object is CommentReference));
        Assert.All(
            reopened.Marks.Where(static item => item.Mark is CommentRangeStart),
            static item => Assert.Equal(0, item.Offset));
        Assert.Equal(
            new[] { 8, 9 },
            reopened.Marks
                .Where(static item => item.Mark is CommentRangeEnd)
                .Select(static item => item.Offset)
                .Order());
    }

    [Fact]
    public void UnsupportedResolvedStateAndReactions_AreReportedPrecisely()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment comment = document.AddComment(paragraph, 0, 8, "Done.");
        comment.IsResolved = true;
        comment.ExtensibleExtLstXml = "<w16cex:extLst/>";

        RtfExportResult result = RtfWriter.Save(document);

        Assert.Contains(result.Diagnostics, static warning => warning.Subject == "comment-resolved-state");
        Assert.Contains(result.Diagnostics, static warning => warning.Subject == "comment-reactions");
        Assert.DoesNotContain(result.Diagnostics, static warning => warning.Subject == "comments");
    }

    [Fact]
    public void PlainUnicodeAndSyntaxCharacters_RoundTripSemantically()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Latin {slash\\} Кириллица 😀\tend");
        paragraph.AppendBreak();
        paragraph.AppendText("next line");
        document.Sections[0].Blocks.Add(paragraph);

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.Equal(document.GetText(), imported.Document.GetText());
        Assert.True(exported.Diagnostics.IsEmpty);
        Assert.DoesNotContain("Кириллица", exported.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"\{slash\\\}", exported.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportIsDeterministic()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("same input"));

        byte[] first = RtfWriter.Save(document).Content.ToArray();
        byte[] second = RtfWriter.Save(document).Content.ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void ParagraphTabStops_RoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("a\tb")
        {
            Format = ParagraphFormat.Default with
            {
                Tabs = new[]
                {
                    new TabStop(Length.FromTwips(1440), TabAlignment.Center, TabLeader.Dot),
                    new TabStop(Length.FromTwips(2880), TabAlignment.Bar, TabLeader.Heavy),
                },
            },
        };
        document.Sections[0].Blocks.Add(paragraph);

        RtfExportResult exported = RtfWriter.Save(document);
        Paragraph imported = Assert.IsType<Paragraph>(RtfReader.Load(exported.Content.Span).Document.Sections[0].Blocks[0]);

        Assert.Equal(paragraph.Format.Tabs, imported.Format.Tabs);
        Assert.True(exported.Diagnostics.IsEmpty);
    }

    [Fact]
    public void Sections_RoundTripWithoutAnExtraParagraph()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("first"));
        document.Sections.Add().Blocks.Add(new Paragraph("second"));

        WordDocument imported = RtfReader.Load(RtfWriter.Save(document).Content.Span).Document;

        Assert.Equal(2, imported.Sections.Count);
        Assert.Equal("first", imported.Sections[0].GetText());
        Assert.Equal("second", imported.Sections[1].GetText());
    }

    [Fact]
    public void CommonCharacterAndParagraphFormatting_RoundTrips()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("formatted", RunFormat.Default with
        {
            FontAscii = "Arial",
            Bold = true,
            Italic = false,
            Color = WordColor.FromRgb(255, 0, 0),
            Size = Length.FromHalfPoints(32),
            Highlight = HighlightColor.Yellow,
            Underline = UnderlineStyle.Double,
            VerticalAlignment = VerticalTextAlignment.Superscript,
        })
        {
            Format = ParagraphFormat.Default with
            {
                Alignment = ParagraphAlignment.Center,
                IndentLeft = Length.FromTwips(720),
                IndentHanging = Length.FromTwips(240),
                SpacingAfter = Length.FromTwips(180),
                KeepWithNext = true,
            },
        };
        document.Sections[0].Blocks.Add(paragraph);

        RtfExportResult result = RtfWriter.Save(document);
        Paragraph imported = Assert.IsType<Paragraph>(RtfReader.Load(result.Content.Span).Document.Sections[0].Blocks[0]);
        RunFormat format = imported.Runs.Single().Format;

        Assert.Equal("formatted", imported.Text);
        Assert.Equal("Arial", format.FontAscii);
        Assert.True(format.Bold);
        Assert.False(format.Italic);
        Assert.Equal(WordColor.FromRgb(255, 0, 0), format.Color);
        Assert.Equal(Length.FromHalfPoints(32), format.Size);
        Assert.Equal(HighlightColor.Yellow, format.Highlight);
        Assert.Equal(UnderlineStyle.Double, format.Underline);
        Assert.Equal(VerticalTextAlignment.Superscript, format.VerticalAlignment);
        Assert.Equal(ParagraphAlignment.Center, imported.Format.Alignment);
        Assert.Equal(Length.FromTwips(720), imported.Format.IndentLeft);
        Assert.Equal(Length.FromTwips(240), imported.Format.IndentHanging);
        Assert.Equal(Length.FromTwips(180), imported.Format.SpacingAfter);
        Assert.True(imported.Format.KeepWithNext);
        Assert.True(result.Diagnostics.IsEmpty);
    }

    [Fact]
    public void UnsupportedBlocks_AreFlattenedAndReported()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(Table.Create(1, 1));
        document.Sections[0].Blocks.Tables.Single()[0, 0].SetText("cell text");

        RtfExportResult result = RtfWriter.Save(document);

        Assert.Equal("cell text", RtfReader.Load(result.Content.Span).Document.GetText());
        Assert.Contains(result.Diagnostics, warning => warning.Subject == nameof(Table));
    }

    [Fact]
    public void UnsupportedRunFormatting_IsReportedWithoutDroppingText()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph(
            "equation marker",
            RunFormat.Default with { OfficeMath = true }));

        RtfExportResult result = RtfWriter.Save(document);

        Assert.Equal("equation marker", RtfReader.Load(result.Content.Span).Document.GetText());
        Assert.Contains(result.Diagnostics, warning => warning.Subject == "run-format");
    }

    [Fact]
    public async Task AsyncStreamExport_LeavesTheStreamOpen()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("stream"));
        using var stream = new MemoryStream();

        RtfExportDiagnostics diagnostics = await RtfWriter.SaveAsync(
            document,
            stream,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(stream.CanWrite);
        Assert.True(diagnostics.IsEmpty);
        Assert.StartsWith(@"{\rtf1", Encoding.ASCII.GetString(stream.ToArray()), StringComparison.Ordinal);
    }
}
