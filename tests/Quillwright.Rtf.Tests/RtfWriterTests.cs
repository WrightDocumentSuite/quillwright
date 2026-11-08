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
        comment.Date = new DateTimeOffset(2024, 5, 6, 14, 37, 29, TimeSpan.Zero);
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
        Assert.Contains(@"\atntime\yr2024\mo5\dy6\hr14\min37\sec29", exported.ToString(), StringComparison.Ordinal);
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
        Assert.Equal(new DateTimeOffset(2024, 5, 6, 14, 37, 29, TimeSpan.Zero), first.Date);
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
    public void NonAdjacentReply_UsesTheParentAnnotationIdAndRoundTrips()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment root = document.AddComment(paragraph, 0, 8, "First thread.", "Ada", "AL");
        document.AddComment(paragraph, 0, 8, "Interleaved thread.", "Linus", "LT");
        document.AddReply(root, "Delayed reply.", "Grace", "GH");

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.Contains(@"\atnparent AL", exported.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(@"\atnparent 1", exported.ToString(), StringComparison.Ordinal);
        Assert.Contains(exported.Diagnostics, static warning => warning.Subject == "comment-thread-order");
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());

        Comment importedRoot = imported.Document.Comments.Single(static comment => comment.GetText() == "First thread.");
        Comment importedReply = imported.Document.Comments.Single(static comment => comment.GetText() == "Delayed reply.");
        Assert.Equal(importedRoot.Id, importedReply.ParentId);
    }

    [Fact]
    public void NonAdjacentNestedReply_UsesItsImmediateParentAnnotationId()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment root = document.AddComment(paragraph, 0, 8, "Root.", "Ada", "AR");
        Comment parentReply = document.AddReply(root, "First reply.", "Grace", "PR");
        document.AddComment(paragraph, 0, 8, "Interleaved thread.", "Linus", "IR");
        document.AddReply(parentReply, "Nested reply.", "Margaret", "NR");

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.Contains(@"\atnparent PR", exported.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(exported.Diagnostics, static warning => warning.Subject == "comment-thread-depth");
        Comment importedParent = imported.Document.Comments.Single(static comment => comment.GetText() == "First reply.");
        Comment importedNested = imported.Document.Comments.Single(static comment => comment.GetText() == "Nested reply.");
        Assert.Equal(importedParent.Id, importedNested.ParentId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void NonAdjacentReplyWithoutParentAnnotationId_IsFlattenedAndReported(string? parentInitials)
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment root = document.AddComment(paragraph, 0, 8, "Root.", "Ada", parentInitials);
        document.AddComment(paragraph, 0, 8, "Interleaved thread.", "Linus", "IR");
        document.AddReply(root, "Reply.", "Grace", "RR");

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.DoesNotContain(@"\atnparent ", exported.ToString(), StringComparison.Ordinal);
        Assert.Contains(exported.Diagnostics, static warning => warning.Subject == "comment-parent-annotation-id");
        Assert.Null(imported.Document.Comments.Single(static comment => comment.GetText() == "Reply.").ParentId);
    }

    [Fact]
    public void NonAdjacentReplyWithAmbiguousParentAnnotationId_IsFlattenedAndReported()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment root = document.AddComment(paragraph, 0, 8, "Root.", "Ada", "AA");
        document.AddComment(paragraph, 0, 8, "Same author id.", "Another Ada", "AA");
        document.AddReply(root, "Reply.", "Grace", "RR");

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.DoesNotContain(@"\atnparent AA", exported.ToString(), StringComparison.Ordinal);
        Assert.Contains(exported.Diagnostics, static warning => warning.Subject == "comment-parent-annotation-id");
        Assert.Null(imported.Document.Comments.Single(static comment => comment.GetText() == "Reply.").ParentId);
    }

    [Fact]
    public void FollowUpPromptBody_IsOmittedAndReported()
    {
        const string prompt = "Ask the user to add quarterly figures.";
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Reviewed text.");
        Comment followUp = document.AddComment(paragraph, 0, 8, prompt, "Ada", "AL");
        followUp.IsFollowUp = true;

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.DoesNotContain(prompt, exported.ToString(), StringComparison.Ordinal);
        RtfExportWarning warning = Assert.Single(
            exported.Diagnostics,
            static candidate => candidate.Subject == "comment-follow-up");
        Assert.Equal(RtfExportWarningKind.ContentSkipped, warning.Kind);
        Assert.Contains("prompt body was omitted", warning.Message, StringComparison.Ordinal);

        Comment reopened = Assert.Single(imported.Document.Comments);
        Assert.Equal(string.Empty, reopened.GetText());
        Assert.False(reopened.IsFollowUp);
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());
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
        var control = new BlockContentControl { Tag = "box" };
        control.Blocks.Add(new Paragraph("control text"));
        document.Sections[0].Blocks.Add(control);

        RtfExportResult result = RtfWriter.Save(document);

        Assert.Equal("control text", RtfReader.Load(result.Content.Span).Document.GetText());
        Assert.Contains(result.Diagnostics, warning => warning.Subject == nameof(BlockContentControl));
    }

    [Fact]
    public void Tables_RoundTripWithRowsCellsWidthsShadingAndMerges()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.AddParagraph("Before table.");

        var table = Table.Create(2, 2);
        table.Grid.Add(Length.FromPoints(120));
        table.Grid.Add(Length.FromPoints(180));
        table.Rows[0].Format = table.Rows[0].Format with
        {
            IsHeader = true,
            Height = Length.FromPoints(24),
            HeightRule = HeightRule.Exact,
        };
        table[0, 0].SetText("Merged Header");
        table[0, 0].Format = table[0, 0].Format with
        {
            Shading = Shading.Solid(WordColor.FromRgb(220, 230, 242)),
        };
        table.MergeCells(0, 0, 1, 2);

        table[1, 0].SetText("Row 2 Col 1");
        table[1, 1].SetText("Line 1");
        table[1, 1].AddParagraph("Line 2");
        section.Blocks.Add(table);
        section.AddParagraph("After table.");

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());

        Section reopenedSection = imported.Document.Sections[0];
        Assert.Equal(3, reopenedSection.Blocks.Count);
        Assert.Equal("Before table.", Assert.IsType<Paragraph>(reopenedSection.Blocks[0]).Text);
        Assert.Equal("After table.", Assert.IsType<Paragraph>(reopenedSection.Blocks[2]).Text);

        Table reopenedTable = Assert.IsType<Table>(reopenedSection.Blocks[1]);
        Assert.Equal(2, reopenedTable.Rows.Count);
        Assert.True(reopenedTable.Rows[0].Format.IsHeader);
        Assert.Equal(Length.FromPoints(24), reopenedTable.Rows[0].Format.Height);
        Assert.Equal(HeightRule.Exact, reopenedTable.Rows[0].Format.HeightRule);

        TableCell headerCell = Assert.Single(reopenedTable.Rows[0].Cells);
        Assert.Equal("Merged Header", headerCell.GetText());
        Assert.Equal(2, headerCell.Format.GridSpan);
        Assert.Equal(WordColor.FromRgb(220, 230, 242), headerCell.Format.Shading?.Fill);
        Assert.Equal(Length.FromPoints(300).Twips, headerCell.Format.Width?.Length.Twips);

        Assert.Equal(2, reopenedTable.Rows[1].Cells.Count);
        Assert.Equal("Row 2 Col 1", reopenedTable[1, 0].GetText());
        Assert.Equal("Line 1\nLine 2", reopenedTable[1, 1].GetText());
    }

    [Fact]
    public void ListsAndNumbering_RoundTripWithFormatsAndLevels()
    {
        WordDocument document = WordDocument.Create();
        var abstractDef = new AbstractNumbering { Id = 0 };
        abstractDef.Levels.Add(new NumberingLevel
        {
            Level = 0,
            Start = 1,
            Format = ListNumberFormat.Decimal,
            Text = "%1.",
            ParagraphFormat = ParagraphFormat.Default with
            {
                IndentLeft = Length.FromTwips(720),
                IndentHanging = Length.FromTwips(360),
            },
        });
        abstractDef.Levels.Add(new NumberingLevel
        {
            Level = 1,
            Start = 1,
            Format = ListNumberFormat.Bullet,
            Text = "\u2022",
            ParagraphFormat = ParagraphFormat.Default with
            {
                IndentLeft = Length.FromTwips(1440),
                IndentHanging = Length.FromTwips(360),
            },
        });
        document.Numbering.Definitions.Add(abstractDef);
        document.Numbering.Instances.Add(new NumberingInstance { Id = 1, AbstractId = 0 });

        Paragraph first = document.Sections[0].AddParagraph("First numbered item");
        first.Format = first.Format with { NumberingId = 1, NumberingLevel = 0 };
        Paragraph nested = document.Sections[0].AddParagraph("Nested bullet item");
        nested.Format = nested.Format with { NumberingId = 1, NumberingLevel = 1 };

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());

        AbstractNumbering reopenedAbstract = Assert.Single(imported.Document.Numbering.Definitions);
        Assert.Equal(2, reopenedAbstract.Levels.Count);
        Assert.Equal(ListNumberFormat.Decimal, reopenedAbstract.Levels[0].Format);
        Assert.Equal("%1.", reopenedAbstract.Levels[0].Text);
        Assert.Equal(ListNumberFormat.Bullet, reopenedAbstract.Levels[1].Format);
        Assert.Equal("\u2022", reopenedAbstract.Levels[1].Text);

        Paragraph[] paragraphs = [.. imported.Document.Sections[0].Blocks.Paragraphs];
        Assert.Equal(2, paragraphs.Length);
        Assert.Equal(1, paragraphs[0].Format.NumberingId);
        Assert.Equal(0, paragraphs[0].Format.NumberingLevel);
        Assert.Equal(1, paragraphs[1].Format.NumberingId);
        Assert.Equal(1, paragraphs[1].Format.NumberingLevel);
    }

    [Fact]
    public void HeadersAndFooters_RoundTripAcrossVariants()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Properties.DifferentFirstPage = true;
        section.Headers.GetOrCreate(HeaderFooterKind.Default).AddParagraph("Default Header");
        section.Headers.GetOrCreate(HeaderFooterKind.First).AddParagraph("First Page Header");
        section.Headers.GetOrCreate(HeaderFooterKind.Even).AddParagraph("Even Page Header");
        section.Footers.GetOrCreate(HeaderFooterKind.Default).AddParagraph("Default Footer");
        section.Footers.GetOrCreate(HeaderFooterKind.First).AddParagraph("First Page Footer");
        section.AddParagraph("Main body text.");

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());

        Section reopened = imported.Document.Sections[0];
        Assert.True(reopened.Properties.DifferentFirstPage);
        Assert.Equal("Default Header", reopened.Headers.Default?.GetText());
        Assert.Equal("First Page Header", reopened.Headers.First?.GetText());
        Assert.Equal("Even Page Header", reopened.Headers.Even?.GetText());
        Assert.Equal("Default Footer", reopened.Footers.Default?.GetText());
        Assert.Equal("First Page Footer", reopened.Footers.First?.GetText());
        Assert.Equal("Main body text.", reopened.GetText());
    }

    [Fact]
    public void FootnotesAndEndnotes_RoundTripWithReferencesAndBodies()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Claim one");
        Note footnote = document.AddFootnote(paragraph, "Supporting footnote.");
        footnote.AddParagraph("Second footnote line.");
        paragraph.AppendText(" and claim two");
        document.AddEndnote(paragraph, "Supporting endnote.");
        paragraph.AppendText(".");

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());

        Paragraph reopenedParagraph = imported.Document.Sections[0].Blocks.Paragraphs.Single();
        Assert.Equal("Claim one and claim two.", reopenedParagraph.GetText());
        NoteReference[] refs = [.. reopenedParagraph.Objects.Select(static o => o.Object).OfType<NoteReference>()];
        Assert.Equal(2, refs.Length);
        Assert.False(refs[0].IsEndnote);
        Assert.True(refs[1].IsEndnote);

        Note reopenedFootnote = Assert.Single(imported.Document.Footnotes, static n => n.Kind == NoteKind.Normal);
        Note reopenedEndnote = Assert.Single(imported.Document.Endnotes, static n => n.Kind == NoteKind.Normal);
        Assert.Equal(" Supporting footnote.\nSecond footnote line.", reopenedFootnote.GetText());
        Assert.Equal(" Supporting endnote.", reopenedEndnote.GetText());
    }

    [Fact]
    public void Pictures_RoundTripWithBytesAndDimensions()
    {
        byte[] pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFUlEQVR4nGP8z8DwnwEJMKEL0FMQAG" +
            "0lAgcqA5wCAAAAAElFTkSuQmCC");

        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Logo: ");
        paragraph.AppendPicture(
            ImageData.FromBytes(pngBytes),
            Length.FromPoints(72),
            Length.FromPoints(36));

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());

        Paragraph reopened = imported.Document.Sections[0].Blocks.Paragraphs.Single();
        Assert.Equal("Logo: ", reopened.GetText());
        Picture picture = Assert.Single(reopened.Objects.Select(static o => o.Object).OfType<Picture>());
        Assert.Equal("image/png", picture.Image.ContentType);
        Assert.Equal(pngBytes, picture.Image.Bytes.ToArray());
        Assert.Equal(Length.FromPoints(72).Twips, picture.Width.Twips);
        Assert.Equal(Length.FromPoints(36).Twips, picture.Height.Twips);
        Assert.Single(imported.Document.Media);
    }

    [Fact]
    public void Pictures_RoundTripTheirNameAndAlternativeText()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Logo: ");
        Picture written = paragraph.AppendPicture(ImageData.FromBytes(TwoByTwoPng));
        written.Name = "Логотип {1}";
        written.Description = "Схема \\ проверки\nвторая строка";

        RtfExportResult exported = RtfWriter.Save(document);
        string rtf = Encoding.ASCII.GetString(exported.Content.Span);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.Contains("{\\*\\picprop{\\sp{\\sn wzName}{\\sv ", rtf, StringComparison.Ordinal);
        Assert.Contains("{\\sp{\\sn wzDescription}{\\sv ", rtf, StringComparison.Ordinal);
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());
        Paragraph reopened = imported.Document.Sections[0].Blocks.Paragraphs.Single();
        Assert.Equal("Logo: " + InlineObject.Placeholder, reopened.Text);
        Picture picture = Assert.Single(reopened.Objects.Select(static o => o.Object).OfType<Picture>());
        Assert.Equal("Логотип {1}", picture.Name);
        Assert.Equal("Схема \\ проверки\nвторая строка", picture.Description);
    }

    [Fact]
    public void WordPictureProperties_GiveTheAlternativeTextAndNothingElse()
    {
        string png = Convert.ToHexString(TwoByTwoPng).ToLowerInvariant();
        string rtf =
            "{\\rtf1\\ansi{\\*\\picprop{\\sp{\\sn wzDescription}{\\sv stray}}}Before " +
            "{\\*\\shppict{\\pict{\\*\\picprop\\shplid1025{\\sp{\\sn shapeType}{\\sv 75}}{\\sp{\\sn fFlipH}{\\sv 0}}" +
            "{\\sp{\\sn wzDescription}{\\sv \\u1057?\\u1093?\\u1077?\\u1084?\\u1072?}}{\\sp{\\sn fLine}{\\sv 0}}}" +
            "\\picscalex100\\picscaley100\\picw53\\pich53\\picwgoal30\\pichgoal30\\pngblip " + png + "}}" +
            "{\\nonshppict{\\pict\\wmetafile8\\picw53\\pich53 0100}} after\\par}";

        RtfImportResult imported = RtfReader.Load(Encoding.ASCII.GetBytes(rtf));

        Paragraph paragraph = imported.Document.Sections[0].Blocks.Paragraphs.Single();
        Assert.Equal("Before " + InlineObject.Placeholder + " after", paragraph.Text);
        Picture picture = Assert.Single(paragraph.Objects.Select(static o => o.Object).OfType<Picture>());
        Assert.Equal("Схема", picture.Description);
        Assert.Null(picture.Name);
    }

    private static readonly byte[] TwoByTwoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFUlEQVR4nGP8z8DwnwEJMKEL0FMQAG" +
        "0lAgcqA5wCAAAAAElFTkSuQmCC");

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

    [Fact]
    public void HyperlinksAndBookmarks_RoundTripWithTargetsTooltipsAndOffsets()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("See Wright Docs or jump to Section One.");
        paragraph.AddRange(
            new Hyperlink
            {
                Url = "https://example.com/docs",
                Tooltip = "Documentation Portal",
            },
            start: 4,
            length: 11);
        paragraph.AddRange(
            new Hyperlink
            {
                Anchor = "sec_one",
            },
            start: 27,
            length: 11);
        paragraph.AddMark(new BookmarkStart { Id = 10, Name = "sec_one" }, offset: 27);
        paragraph.AddMark(new BookmarkEnd { Id = 10 }, offset: 38);

        RtfExportResult exported = RtfWriter.Save(document);
        RtfImportResult imported = RtfReader.Load(exported.Content.Span);

        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());

        Paragraph reopened = imported.Document.Sections[0].Blocks.Paragraphs.Single();
        Assert.Equal("See Wright Docs or jump to Section One.", reopened.GetText());

        (int Start, int Length, InlineRange Range)[] ranges = [.. reopened.Ranges];
        Assert.Equal(2, ranges.Length);
        Hyperlink external = Assert.IsType<Hyperlink>(ranges[0].Range);
        Assert.Equal(4, ranges[0].Start);
        Assert.Equal(11, ranges[0].Length);
        Assert.Equal("https://example.com/docs", external.Url);
        Assert.Equal("Documentation Portal", external.Tooltip);

        Hyperlink internalLink = Assert.IsType<Hyperlink>(ranges[1].Range);
        Assert.Equal(27, ranges[1].Start);
        Assert.Equal(11, ranges[1].Length);
        Assert.Equal("sec_one", internalLink.Anchor);

        BookmarkStart bkmkStart = Assert.Single(reopened.Marks, static m => m.Mark is BookmarkStart)
            .Mark as BookmarkStart ?? throw new InvalidOperationException();
        int bkmkStartOffset = Assert.Single(reopened.Marks, static m => m.Mark is BookmarkStart).Offset;
        int bkmkEndOffset = Assert.Single(reopened.Marks, static m => m.Mark is BookmarkEnd).Offset;
        Assert.Equal("sec_one", bkmkStart.Name);
        Assert.Equal(27, bkmkStartOffset);
        Assert.Equal(38, bkmkEndOffset);
    }

    [Fact]
    public void TableAndCellBordersAndNestedTableCells_RoundTripAndPreserveContent()
    {
        WordDocument document = WordDocument.Create();
        var outerBorders = BorderSet.AllWithInside(
            BorderLine.Single(Length.FromPoints(1), WordColor.FromRgb(0x11, 0x22, 0x33)));
        var table = new Table
        {
            Format = TableFormat.Default with { Borders = outerBorders },
        };
        table.Grid.Add(Length.FromTwips(2880));
        table.Grid.Add(Length.FromTwips(2880));

        var row = new TableRow();
        var cell1 = new TableCell
        {
            Format = new TableCellFormat
            {
                Width = TableWidth.FromLength(Length.FromTwips(2880)),
                Borders = new BorderSet
                {
                    Top = new BorderLine
                    {
                        Style = BorderStyle.Double,
                        Width = Length.FromTwips(30),
                        Color = WordColor.FromRgb(0xAA, 0x00, 0x00),
                    },
                    Bottom = new BorderLine
                    {
                        Style = BorderStyle.Dashed,
                        Width = Length.FromTwips(15),
                    },
                },
            },
        };
        cell1.Blocks.Add(new Paragraph("Header cell"));

        var cell2 = new TableCell
        {
            Format = new TableCellFormat
            {
                Width = TableWidth.FromLength(Length.FromTwips(2880)),
            },
        };
        cell2.Blocks.Add(new Paragraph("Before nested"));
        var nested = new Table();
        var nestedRow = new TableRow();
        var nestedCell = new TableCell();
        nestedCell.Blocks.Add(new Paragraph("Inside nested table"));
        nestedRow.Cells.Add(nestedCell);
        nested.Rows.Add(nestedRow);
        cell2.Blocks.Add(nested);
        cell2.Blocks.Add(new Paragraph("After nested"));

        row.Cells.Add(cell1);
        row.Cells.Add(cell2);
        table.Rows.Add(row);
        document.Sections[0].Blocks.Add(table);

        RtfExportResult exported = RtfWriter.Save(document);
        Assert.Contains(exported.Diagnostics, static w => w.Kind == RtfExportWarningKind.UnsupportedBlock && w.Subject == "cell-Table");

        RtfImportResult imported = RtfReader.Load(exported.Content.Span);
        Table reopenedTable = Assert.IsType<Table>(Assert.Single(imported.Document.Sections[0].Blocks));
        Assert.NotNull(reopenedTable.Format.Borders);
        Assert.Equal(BorderStyle.Single, reopenedTable.Format.Borders!.Top!.Style);
        Assert.Equal(WordColor.FromRgb(0x11, 0x22, 0x33), reopenedTable.Format.Borders.Top.Color);
        Assert.Equal(BorderStyle.Single, reopenedTable.Format.Borders.InsideHorizontal!.Style);

        TableCell reopenedCell1 = reopenedTable.Rows[0].Cells[0];
        Assert.NotNull(reopenedCell1.Format.Borders);
        Assert.Equal(BorderStyle.Double, reopenedCell1.Format.Borders!.Top!.Style);
        Assert.Equal(Length.FromTwips(30), reopenedCell1.Format.Borders.Top.Width);
        Assert.Equal(WordColor.FromRgb(0xAA, 0x00, 0x00), reopenedCell1.Format.Borders.Top.Color);
        Assert.Equal(BorderStyle.Dashed, reopenedCell1.Format.Borders.Bottom!.Style);

        TableCell reopenedCell2 = reopenedTable.Rows[0].Cells[1];
        Assert.Equal(
            new[] { "Before nested", "Inside nested table", "After nested" },
            reopenedCell2.Blocks.Paragraphs.Select(static p => p.GetText()));
    }

    [Fact]
    public void CustomParagraphAndCharacterStyles_RoundTripThroughStyleSheet()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("Callout", StyleKind.Paragraph)
        {
            Name = "Callout",
            BasedOn = "Normal",
            NextStyle = "Normal",
            IsCustom = true,
            ParagraphFormat = ParagraphFormat.Default with
            {
                IndentLeft = Length.FromTwips(360),
                SpacingAfter = Length.FromTwips(120),
            },
            RunFormat = RunFormat.Default with
            {
                Italic = true,
            },
        });
        document.Styles.Add(new Style("KeyTerm", StyleKind.Character)
        {
            Name = "KeyTerm",
            IsCustom = true,
            RunFormat = RunFormat.Default with
            {
                Bold = true,
                Color = WordColor.FromRgb(0x00, 0x44, 0x88),
            },
        });

        var paragraph = new Paragraph
        {
            Format = ParagraphFormat.Default with { StyleId = "Callout" },
        };
        paragraph.AppendText("Important ", RunFormat.Default);
        paragraph.AppendText("concept", RunFormat.Default with { StyleId = "KeyTerm" });
        document.Sections[0].Blocks.Add(paragraph);

        RtfExportResult exported = RtfWriter.Save(document);
        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());
        Assert.Contains(@"\stylesheet", exported.ToString(), StringComparison.Ordinal);

        RtfImportResult imported = RtfReader.Load(exported.Content.Span);
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());

        Style? callout = imported.Document.Styles.Find("Callout");
        Assert.NotNull(callout);
        Assert.Equal(StyleKind.Paragraph, callout!.Kind);
        Assert.Equal("Normal", callout.BasedOn);
        Assert.Equal(Length.FromTwips(360), callout.ParagraphFormat.IndentLeft);
        Assert.Equal(true, callout.RunFormat.Italic);

        Style? keyTerm = imported.Document.Styles.Find("KeyTerm");
        Assert.NotNull(keyTerm);
        Assert.Equal(StyleKind.Character, keyTerm!.Kind);
        Assert.Equal(true, keyTerm.RunFormat.Bold);
        Assert.Equal(WordColor.FromRgb(0x00, 0x44, 0x88), keyTerm.RunFormat.Color);

        Paragraph reopened = imported.Document.Sections[0].Blocks.Paragraphs.Single();
        Assert.Equal("Callout", reopened.Format.StyleId);
        Assert.Contains(reopened.Runs, static r => r.Format.StyleId == "KeyTerm" && r.Text.ToString() == "concept");
    }

    [Fact]
    public void SectionPageGeometryAndParagraphBorders_RoundTripCleanly()
    {
        WordDocument document = WordDocument.Create();
        Section s1 = document.Sections[0];
        s1.Properties.Orientation = PageOrientation.Landscape;
        s1.Properties.PageWidth = Length.FromTwips(16838);
        s1.Properties.PageHeight = Length.FromTwips(11906);
        s1.Properties.Margins.Left = Length.FromTwips(1080);
        s1.Properties.Margins.Right = Length.FromTwips(1080);
        s1.Properties.Margins.Top = Length.FromTwips(900);
        s1.Properties.Margins.Bottom = Length.FromTwips(900);
        s1.Properties.Margins.Gutter = Length.FromTwips(180);
        s1.Properties.Margins.Header = Length.FromTwips(540);
        s1.Properties.Margins.Footer = Length.FromTwips(600);
        s1.Properties.Columns.Count = 2;
        s1.Properties.Columns.Space = Length.FromTwips(480);
        s1.Properties.Columns.Separator = true;

        var borderedParagraph = new Paragraph("Callout box")
        {
            Format = ParagraphFormat.Default with
            {
                Shading = Shading.Solid(WordColor.FromRgb(0xF0, 0xF4, 0xF8)),
                Borders = new BorderSet
                {
                    Top = new BorderLine
                    {
                        Style = BorderStyle.Single,
                        Width = Length.FromTwips(15),
                        Color = WordColor.FromRgb(0x22, 0x44, 0x66),
                        Space = Length.FromTwips(80),
                    },
                    Bottom = new BorderLine
                    {
                        Style = BorderStyle.Double,
                        Width = Length.FromTwips(30),
                        Color = WordColor.FromRgb(0x22, 0x44, 0x66),
                    },
                    Left = new BorderLine
                    {
                        Style = BorderStyle.Thick,
                        Width = Length.FromTwips(45),
                        Color = WordColor.FromRgb(0xCC, 0x33, 0x00),
                    },
                    Bar = new BorderLine
                    {
                        Style = BorderStyle.Single,
                        Width = Length.FromTwips(15),
                        Color = WordColor.FromRgb(0x00, 0x88, 0x44),
                    },
                },
            },
        };
        s1.Blocks.Add(borderedParagraph);

        Section s2 = document.Sections.Add();
        s2.Properties.Start = SectionStart.Continuous;
        s2.Properties.Columns.EqualWidth = false;
        s2.Properties.Columns.Count = 2;
        s2.Properties.Columns.Columns.Add(new TextColumn(Length.FromTwips(3600), Length.FromTwips(360)));
        s2.Properties.Columns.Columns.Add(new TextColumn(Length.FromTwips(5400), Length.Zero));
        s2.AddParagraph("Second section");

        RtfExportResult exported = RtfWriter.Save(document);
        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());

        RtfImportResult imported = RtfReader.Load(exported.Content.Span);
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());
        Assert.Equal(2, imported.Document.Sections.Count);

        Section reopened1 = imported.Document.Sections[0];
        Assert.Equal(PageOrientation.Landscape, reopened1.Properties.Orientation);
        Assert.Equal(16838, reopened1.Properties.PageWidth.Twips);
        Assert.Equal(11906, reopened1.Properties.PageHeight.Twips);
        Assert.Equal(1080, reopened1.Properties.Margins.Left.Twips);
        Assert.Equal(1080, reopened1.Properties.Margins.Right.Twips);
        Assert.Equal(900, reopened1.Properties.Margins.Top.Twips);
        Assert.Equal(900, reopened1.Properties.Margins.Bottom.Twips);
        Assert.Equal(180, reopened1.Properties.Margins.Gutter.Twips);
        Assert.Equal(540, reopened1.Properties.Margins.Header.Twips);
        Assert.Equal(600, reopened1.Properties.Margins.Footer.Twips);
        Assert.Equal(2, reopened1.Properties.Columns.Count);
        Assert.Equal(480, reopened1.Properties.Columns.Space.Twips);
        Assert.True(reopened1.Properties.Columns.Separator);

        Paragraph reopenedParagraph = Assert.Single(reopened1.Blocks.Paragraphs);
        Assert.Equal(WordColor.FromRgb(0xF0, 0xF4, 0xF8), reopenedParagraph.Format.Shading?.Fill);
        Assert.NotNull(reopenedParagraph.Format.Borders);
        Assert.Equal(BorderStyle.Single, reopenedParagraph.Format.Borders!.Top?.Style);
        Assert.Equal(Length.FromTwips(80), reopenedParagraph.Format.Borders.Top?.Space);
        Assert.Equal(WordColor.FromRgb(0x22, 0x44, 0x66), reopenedParagraph.Format.Borders.Top?.Color);
        Assert.Equal(BorderStyle.Double, reopenedParagraph.Format.Borders.Bottom?.Style);
        Assert.Equal(BorderStyle.Thick, reopenedParagraph.Format.Borders.Left?.Style);
        Assert.Equal(WordColor.FromRgb(0xCC, 0x33, 0x00), reopenedParagraph.Format.Borders.Left?.Color);
        Assert.Equal(BorderStyle.Single, reopenedParagraph.Format.Borders.Bar?.Style);

        Section reopened2 = imported.Document.Sections[1];
        Assert.Equal(SectionStart.Continuous, reopened2.Properties.Start);
        Assert.False(reopened2.Properties.Columns.EqualWidth);
        Assert.Equal(2, reopened2.Properties.Columns.Columns.Count);
        Assert.Equal(3600, reopened2.Properties.Columns.Columns[0].Width.Twips);
        Assert.Equal(360, reopened2.Properties.Columns.Columns[0].Space.Twips);
        Assert.Equal(5400, reopened2.Properties.Columns.Columns[1].Width.Twips);
    }

    [Fact]
    public void DocumentProperties_RoundTripThroughInfoGroup()
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Title = "Quarterly Architecture Review";
        document.Properties.Subject = "System Design & Conformance";
        document.Properties.Creator = "Ada Lovelace";
        document.Properties.Keywords = "rtf, metadata, conformance";
        document.Properties.Description = "Prepared for the architecture committee.";
        document.Properties.LastModifiedBy = "Grace Hopper";
        document.Properties.Category = "Engineering";
        document.Properties.Revision = "7";
        document.Properties.Created = new DateTimeOffset(2026, 4, 15, 9, 30, 45, TimeSpan.Zero);
        document.Properties.Modified = new DateTimeOffset(2026, 10, 17, 14, 20, 10, TimeSpan.Zero);
        document.Sections[0].AddParagraph("Body paragraph");

        RtfExportResult exported = RtfWriter.Save(document);
        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());

        RtfImportResult imported = RtfReader.Load(exported.Content.Span);
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());

        DocumentProperties reopened = imported.Document.Properties;
        Assert.Equal("Quarterly Architecture Review", reopened.Title);
        Assert.Equal("System Design & Conformance", reopened.Subject);
        Assert.Equal("Ada Lovelace", reopened.Creator);
        Assert.Equal("rtf, metadata, conformance", reopened.Keywords);
        Assert.Equal("Prepared for the architecture committee.", reopened.Description);
        Assert.Equal("Grace Hopper", reopened.LastModifiedBy);
        Assert.Equal("Engineering", reopened.Category);
        Assert.Equal("7", reopened.Revision);
        Assert.Equal(new DateTimeOffset(2026, 4, 15, 9, 30, 45, TimeSpan.Zero), reopened.Created);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 14, 20, 10, TimeSpan.Zero), reopened.Modified);
        Assert.Equal("Body paragraph", Assert.Single(imported.Document.Sections[0].Blocks.Paragraphs).Text);
    }

    [Fact]
    public void ListStartOverride_RoundTripsThroughLfoLevel()
    {
        WordDocument document = WordDocument.Create();
        int numId = document.Numbering.AddNumberedList();
        NumberingInstance instance = Assert.Single(document.Numbering.Instances, i => i.Id == numId);
        instance.Overrides.Add(new NumberingLevelOverride { Level = 0, StartOverride = 5 });
        instance.Overrides.Add(new NumberingLevelOverride { Level = 2, StartOverride = 3 });

        document.Sections[0].Blocks.Add(new Paragraph("Fifth item")
        {
            Format = ParagraphFormat.Default with { NumberingId = numId, NumberingLevel = 0 },
        });

        RtfExportResult exported = RtfWriter.Save(document);
        Assert.True(exported.Diagnostics.IsEmpty, exported.Diagnostics.ToString());

        RtfImportResult imported = RtfReader.Load(exported.Content.Span);
        Assert.True(imported.Diagnostics.IsEmpty, imported.Diagnostics.ToString());

        NumberingInstance reopenedInstance = Assert.Single(imported.Document.Numbering.Instances);
        Assert.Equal(5, Assert.Single(reopenedInstance.Overrides, o => o.Level == 0).StartOverride);
        Assert.Equal(3, Assert.Single(reopenedInstance.Overrides, o => o.Level == 2).StartOverride);
    }
}
