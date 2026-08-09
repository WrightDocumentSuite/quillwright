using System.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Rtf.Tests;

public class RtfReaderTests
{
    [Fact]
    public void PlainTextAndParagraphs_AreImported()
    {
        RtfImportResult result = RtfReader.Load(Ascii(@"{\rtf1\ansi First paragraph.\par Second paragraph.}"));

        Assert.Equal("First paragraph.\nSecond paragraph.", result.Document.GetText());
        Assert.True(result.Diagnostics.IsEmpty);
    }

    [Fact]
    public void EscapedSyntaxAndSpecialCharacters_AreImportedAsText()
    {
        RtfImportResult result = RtfReader.Load(
            Ascii(@"{\rtf1 braces \{x\}, slash \\, nonbreaking\~space, soft\-hyphen, fixed\_hyphen.}"));

        Assert.Equal("braces {x}, slash \\, nonbreaking\u00A0space, soft\u00ADhyphen, fixed\u2011hyphen.", result.Document.GetText());
    }

    [Fact]
    public void UnicodeControl_DropsItsConfiguredFallback()
    {
        RtfImportResult result = RtfReader.Load(Ascii(@"{\rtf1\ansi\uc1 Cyrillic: \u1040?; Greek: \u945x.}"));

        Assert.Equal("Cyrillic: А; Greek: α.", result.Document.GetText());
    }

    [Fact]
    public void UnicodeFallback_ControlSymbolCountsAsOneCharacter()
    {
        RtfImportResult result = RtfReader.Load(Ascii(@"{\rtf1\ansi\uc1 \u1040\'3fB}"));

        Assert.Equal("АB", result.Document.GetText());
    }

    [Fact]
    public void UnicodePair_PrefersTheUdBranch()
    {
        RtfImportResult result = RtfReader.Load(
            Ascii(@"{\rtf1{\upr{ANSI value}{\*\ud Unicode \u915? value}}}"));

        Assert.Equal("Unicode Γ value", result.Document.GetText());
    }

    [Fact]
    public void ConsecutiveHexBytes_AreDecodedTogetherForDoubleByteCodePages()
    {
        RtfImportResult result = RtfReader.Load(Ascii(@"{\rtf1\ansi\ansicpg932 Japanese: \'93\'fa}"));

        Assert.Equal("Japanese: 日", result.Document.GetText());
    }

    [Fact]
    public void HeaderTablesAndUnknownStarredDestinations_DoNotLeakIntoBodyText()
    {
        RtfImportResult result = RtfReader.Load(Ascii(
            @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}{\colortbl;\red255\green0\blue0;}{\*\private secret}Visible}"));

        Assert.Equal("Visible", result.Document.GetText());
    }

    [Fact]
    public void NestedCharacterFormatting_IsScopedByGroups()
    {
        RtfImportResult result = RtfReader.Load(Ascii(@"{\rtf1 plain {\b bold {\i both} bold} plain}"));
        Paragraph paragraph = Assert.IsType<Paragraph>(result.Document.Sections[0].Blocks[0]);

        Assert.Collection(
            paragraph.Runs,
            run =>
            {
                Assert.Equal("plain ", run.Text);
                Assert.Null(run.Format.Bold);
                Assert.Null(run.Format.Italic);
            },
            run =>
            {
                Assert.Equal("bold ", run.Text);
                Assert.True(run.Format.Bold);
                Assert.Null(run.Format.Italic);
            },
            run =>
            {
                Assert.Equal("both", run.Text);
                Assert.True(run.Format.Bold);
                Assert.True(run.Format.Italic);
            },
            run =>
            {
                Assert.Equal(" bold", run.Text);
                Assert.True(run.Format.Bold);
                Assert.Null(run.Format.Italic);
            },
            run =>
            {
                Assert.Equal(" plain", run.Text);
                Assert.Null(run.Format.Bold);
                Assert.Null(run.Format.Italic);
            });
    }

    [Fact]
    public void FontTable_SelectsFontAndItsCharacterEncoding()
    {
        RtfImportResult result = RtfReader.Load(Ascii(
            @"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fcharset0 Arial;}{\f1\fcharset204 Times New Roman;}}\f1 Cyrillic: \'cf\'f0}"));
        Paragraph paragraph = Assert.IsType<Paragraph>(result.Document.Sections[0].Blocks[0]);

        Assert.Equal("Cyrillic: Пр", paragraph.Text);
        Assert.Equal("Arial", result.Document.Styles.DefaultRunFormat.FontAscii);
        Assert.All(paragraph.Runs, run => Assert.Equal("Times New Roman", run.Format.FontAscii));
    }

    [Fact]
    public void UngroupedFontEntries_DoNotInheritThePreviousCharset()
    {
        RtfImportResult result = RtfReader.Load(Ascii(
            @"{\rtf1\ansi\ansicpg1252{\fonttbl\f0\fcharset204 Cyrillic;\f1 Arial;}\f1 \'e9}"));
        Paragraph paragraph = Assert.IsType<Paragraph>(result.Document.Sections[0].Blocks[0]);

        Assert.Equal("é", paragraph.Text);
        Assert.Equal("Arial", paragraph.Runs.Single().Format.FontAscii);
    }

    [Fact]
    public void ColorsAndCommonCharacterProperties_AreImported()
    {
        RtfImportResult result = RtfReader.Load(Ascii(
            @"{\rtf1{\colortbl;\red255\green0\blue0;}\cf1\fs32\uldb\super red}"));
        RunFormat format = Assert.IsType<Paragraph>(result.Document.Sections[0].Blocks[0]).Runs.Single().Format;

        Assert.Equal(WordColor.FromRgb(255, 0, 0), format.Color);
        Assert.Equal(Length.FromHalfPoints(32), format.Size);
        Assert.Equal(UnderlineStyle.Double, format.Underline);
        Assert.Equal(VerticalTextAlignment.Superscript, format.VerticalAlignment);
    }

    [Fact]
    public void CommonParagraphProperties_AreImportedAndResetByPard()
    {
        RtfImportResult result = RtfReader.Load(Ascii(
            @"{\rtf1\qc\li720\ri360\fi-240\sb120\sa180\sl-300\slmult0\keep\keepn first\par\pard second}"));
        Paragraph first = Assert.IsType<Paragraph>(result.Document.Sections[0].Blocks[0]);
        Paragraph second = Assert.IsType<Paragraph>(result.Document.Sections[0].Blocks[1]);

        Assert.Equal(ParagraphAlignment.Center, first.Format.Alignment);
        Assert.Equal(Length.FromTwips(720), first.Format.IndentLeft);
        Assert.Equal(Length.FromTwips(360), first.Format.IndentRight);
        Assert.Equal(Length.FromTwips(240), first.Format.IndentHanging);
        Assert.Equal(Length.FromTwips(120), first.Format.SpacingBefore);
        Assert.Equal(Length.FromTwips(180), first.Format.SpacingAfter);
        Assert.Equal(Length.FromTwips(300), first.Format.LineSpacing);
        Assert.Equal(LineSpacingRule.Exact, first.Format.LineSpacingRule);
        Assert.True(first.Format.KeepLinesTogether);
        Assert.True(first.Format.KeepWithNext);
        Assert.Equal(ParagraphFormat.Default, second.Format);
    }

    [Fact]
    public void ParagraphTabStops_AreImported()
    {
        RtfImportResult result = RtfReader.Load(Ascii(
            @"{\rtf1\pard\tqc\tldot\tx1440\tqr\tlhyph\tx2880\tlth\tb3600 tabs}"));
        Paragraph paragraph = Assert.IsType<Paragraph>(result.Document.Sections[0].Blocks[0]);

        Assert.Equal(
            new[]
            {
                new TabStop(Length.FromTwips(1440), TabAlignment.Center, TabLeader.Dot),
                new TabStop(Length.FromTwips(2880), TabAlignment.Right, TabLeader.Hyphen),
                new TabStop(Length.FromTwips(3600), TabAlignment.Bar, TabLeader.Heavy),
            },
            paragraph.Format.Tabs);
    }

    [Fact]
    public void AFieldImportsItsResultButNotItsInstruction()
    {
        RtfImportResult result = RtfReader.Load(Ascii(
            @"{\rtf1 before {\field{\*\fldinst HYPERLINK ""https://example.com""}{\fldrslt visible result}} after}"));

        Assert.Equal("before visible result after", result.Document.GetText());
    }

    [Fact]
    public void WordStyleAnnotations_ImportRangesMetadataBodiesAndReplies()
    {
        RtfImportResult result = RtfReader.Load(Ascii(
            @"{\rtf1\ansi Alpha {\*\atrfstart 100}{\*\atrfstart 101}beta" +
            @"{\*\atrfend 100}{\*\atnid AL}{\*\atnauthor Ada Lovelace}\chatn " +
            @"{\*\annotation{\*\atnref 100}{\*\atndate 132664367}\pard\plain {\chatn }First note\par}" +
            @"{\*\atrfend 101}{\*\atnid GH}{\*\atnauthor Grace Hopper}\chatn " +
            @"{\*\annotation{\*\atnref 101}{\*\atndate 132664367}{\*\atnparent -1}" +
            @"\pard\plain {\chatn }Reply note\par} gamma.}"));

        Assert.True(result.Diagnostics.IsEmpty, result.Diagnostics.ToString());
        Assert.Equal("Alpha beta gamma.", result.Document.GetText());
        Assert.Equal(2, result.Document.Comments.Count);

        Comment parent = result.Document.Comments.Single(static comment => comment.ParentId is null);
        Comment reply = result.Document.Comments.Single(static comment => comment.ParentId is not null);
        Assert.Equal("First note", parent.GetText());
        Assert.Equal("Ada Lovelace", parent.Author);
        Assert.Equal("AL", parent.Initials);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 16, 47, 0, TimeSpan.Zero), parent.Date);
        Assert.Equal("Reply note", reply.GetText());
        Assert.Equal("Grace Hopper", reply.Author);
        Assert.Equal("GH", reply.Initials);
        Assert.Equal(parent.Id, reply.ParentId);

        Paragraph paragraph = result.Document.Sections[0].Blocks.Paragraphs.Single();
        Assert.All(
            paragraph.Marks.Where(static item => item.Mark is CommentRangeStart),
            static item => Assert.Equal(6, item.Offset));
        Assert.Equal(
            new[] { 10, 11 },
            paragraph.Marks
                .Where(static item => item.Mark is CommentRangeEnd)
                .Select(static item => item.Offset)
                .Order());
        Assert.Equal(2, paragraph.Objects.Count(static item => item.Object is CommentReference));
    }

    [Fact]
    public void MalformedAnnotationAnchorsAndParents_AreRecoveredWithDiagnostics()
    {
        RtfImportResult result = RtfReader.Load(Ascii(
            @"{\rtf1\ansi Before {\*\atrfstart partial}range" +
            @"{\*\atnid O}{\*\atnauthor Orphan}\chatn " +
            @"{\*\annotation{\*\atnref partial}{\*\atnparent missing}\pard Broken note\par}" +
            @" after{\*\atrfstart unused}}"));

        Comment comment = Assert.Single(result.Document.Comments);
        Assert.Equal("Broken note", comment.GetText());
        Assert.Null(comment.ParentId);
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == RtfImportWarningKind.MalformedAnnotation && warning.Subject == "annotation-anchor");
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == RtfImportWarningKind.MalformedAnnotation && warning.Subject == "annotation-parent");
        Assert.Contains(result.Diagnostics, static warning =>
            warning.Kind == RtfImportWarningKind.MalformedAnnotation && warning.Subject == "annotation-orphan-anchor");
    }

    [Fact]
    public void AnnotationWithoutRangeBookmark_BecomesAPointComment()
    {
        RtfImportResult result = RtfReader.Load(Ascii(
            @"{\rtf1\ansi Text{\*\atnid A}{\*\atnauthor Ada}\chatn " +
            @"{\*\annotation\pard Point note\par}}"));

        Assert.True(result.Diagnostics.IsEmpty, result.Diagnostics.ToString());
        Comment comment = Assert.Single(result.Document.Comments);
        Assert.Equal("Point note", comment.GetText());
        Paragraph paragraph = result.Document.Sections[0].Blocks.Paragraphs.Single();
        Assert.Equal(4, paragraph.Marks.Single(static item => item.Mark is CommentRangeStart).Offset);
        Assert.Equal(4, paragraph.Marks.Single(static item => item.Mark is CommentRangeEnd).Offset);
        Assert.Equal(4, paragraph.Objects.Single(static item => item.Object is CommentReference).Offset);
    }

    [Fact]
    public void BinaryPayload_MayContainRtfDelimiterBytes()
    {
        byte[] prefix = Ascii(@"{\rtf1 before{\pict\bin3 ");
        byte[] suffix = Ascii("}after}");
        byte[] content = [.. prefix, (byte)'{', (byte)'\\', (byte)'}', .. suffix];

        RtfImportResult result = RtfReader.Load(content);

        Assert.Equal("beforeafter", result.Document.GetText());
        Assert.Contains(result.Diagnostics, warning => warning.Subject == "Picture");
    }

    [Fact]
    public void LinePageAndColumnControls_BecomeBreakObjects()
    {
        RtfImportResult result = RtfReader.Load(Ascii(@"{\rtf1 a\line b\page c\column d}"));
        Paragraph paragraph = Assert.IsType<Paragraph>(result.Document.Sections[0].Blocks[0]);

        Assert.Collection(
            paragraph.Objects.Select(static item => item.Object),
            value => Assert.Equal(BreakKind.Line, Assert.IsType<Break>(value).Kind),
            value => Assert.Equal(BreakKind.Page, Assert.IsType<Break>(value).Kind),
            value => Assert.Equal(BreakKind.Column, Assert.IsType<Break>(value).Kind));
    }

    [Fact]
    public void TruncatedBinaryPayload_IsRejected()
    {
        RtfFormatException exception = Assert.Throws<RtfFormatException>(
            () => RtfReader.Load(Ascii(@"{\rtf1{\pict\bin8 abc}}")));

        Assert.Contains("binary payload", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnbalancedGroups_AreRejected()
    {
        Assert.Throws<RtfFormatException>(() => RtfReader.Load(Ascii(@"{\rtf1 missing close")));
        Assert.Throws<RtfFormatException>(() => RtfReader.Load(Ascii(@"{\rtf1}extra")));
    }

    [Fact]
    public void ConfiguredNestingLimit_IsEnforced()
    {
        var options = new RtfImportOptions { MaxGroupDepth = 2 };

        Assert.Throws<RtfFormatException>(() => RtfReader.Load(Ascii(@"{\rtf1{{too deep}}}"), options));
    }

    [Fact]
    public void ConfiguredTextLimit_IsEnforced()
    {
        var options = new RtfImportOptions { MaxTextCharacters = 4 };

        Assert.Throws<RtfFormatException>(() => RtfReader.Load(Ascii(@"{\rtf1 five!}"), options));
    }

    [Fact]
    public async Task AsyncStreamImport_LeavesTheStreamOpen()
    {
        using var stream = new MemoryStream(Ascii(@"{\rtf1 async}"));

        RtfImportResult result = await RtfReader.LoadAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("async", result.Document.GetText());
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task AsyncStreamImport_StopsAtTheInputLimit()
    {
        using var stream = new MemoryStream(Ascii(@"{\rtf1 too large}"));
        var options = new RtfImportOptions { MaxInputBytes = 8 };

        await Assert.ThrowsAsync<RtfFormatException>(
            async () => await RtfReader.LoadAsync(
                stream,
                options,
                TestContext.Current.CancellationToken));
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
}
