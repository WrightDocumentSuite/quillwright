using Quillwright.Doc.Writing;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc.Tests;

/// <summary>
/// The binary format expresses structure with reserved characters inside one flat stream, so
/// the assembler's job is almost entirely about putting the right character in the right
/// place. These tests read the stream back as characters and check exactly that.
/// </summary>
public class StoryAssemblerTests
{
    [Fact]
    public void EachParagraph_EndsWithAParagraphMark()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("First"));
        Add(document, new Paragraph("Second"));

        StoryAssembler story = Assemble(document);

        Assert.Equal("First\rSecond\r", story.Text);
        Assert.Equal(2, story.Paragraphs.Count);
        Assert.Equal(6, story.Paragraphs[0].EndPosition);
        Assert.Equal(13, story.Paragraphs[1].EndPosition);
    }

    [Fact]
    public void AParagraphMark_CarriesTheParagraphsOwnProperties()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Centred") { Format = ParagraphFormat.Default with { Alignment = ParagraphAlignment.Center } };
        Add(document, paragraph);

        StoryAssembler story = Assemble(document);
        ParagraphFormat parsed = SprmTranslator.ApplyParagraph(ParagraphFormat.Default, story.Paragraphs[0].Properties, out _);

        Assert.Equal(ParagraphAlignment.Center, parsed.Alignment);
    }

    [Fact]
    public void EveryCharacter_IsCoveredByExactlyOneRun()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendText("plain ");
        paragraph.AppendText("bold", RunFormat.Default with { Bold = true });
        Add(document, paragraph);

        StoryAssembler story = Assemble(document);

        Assert.Equal(0, story.Runs[0].StartPosition);
        Assert.Equal(story.Text.Length, story.Runs[^1].EndPosition);
        for (int i = 1; i < story.Runs.Count; i++)
            Assert.Equal(story.Runs[i - 1].EndPosition, story.Runs[i].StartPosition);
    }

    [Fact]
    public void AllButTheLastSection_EndOnASectionMark()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("One"));
        var second = new Section();
        second.Blocks.Add(new Paragraph("Two"));
        document.Sections.Add(second);

        StoryAssembler story = Assemble(document);

        Assert.Equal("One\fTwo\r", story.Text);
        Assert.Equal(2, story.Sections.Count);
        Assert.Equal(0, story.Sections[0].StartPosition);
        Assert.Equal(4, story.Sections[1].StartPosition);
    }

    [Fact]
    public void ASectionEndingInATable_GetsAParagraphToCarryTheBreak()
    {
        WordDocument document = WordDocument.Create();
        Add(document, NewTable("cell"));
        var second = new Section();
        second.Blocks.Add(new Paragraph("after"));
        document.Sections.Add(second);

        StoryAssembler story = Assemble(document);

        Assert.EndsWith("\fafter\r", story.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMainStory_AlwaysEndsWithAParagraphMark()
    {
        WordDocument document = WordDocument.Create();
        Add(document, NewTable("only a table"));

        StoryAssembler story = Assemble(document);

        Assert.EndsWith("\r", story.Text, StringComparison.Ordinal);
        Assert.Equal(story.Text.Length, story.MainLength);
    }

    [Fact]
    public void ATable_IsWrittenAsCellMarksFollowedByARowMark()
    {
        WordDocument document = WordDocument.Create();
        Add(document, NewTable("a", "b"));

        StoryAssembler story = Assemble(document);

        Assert.StartsWith("a\ab\a\a", story.Text, StringComparison.Ordinal);

        SprmTranslator.ApplyParagraph(ParagraphFormat.Default, story.Paragraphs[0].Properties, out DocParagraphFlags cell);
        SprmTranslator.ApplyParagraph(ParagraphFormat.Default, story.Paragraphs[2].Properties, out DocParagraphFlags rowEnd);

        Assert.True(cell.InTable);
        Assert.False(cell.IsRowEnd);
        Assert.True(rowEnd.InTable);
        Assert.True(rowEnd.IsRowEnd);
    }

    [Fact]
    public void ARowMark_CarriesTheTableDefinition()
    {
        WordDocument document = WordDocument.Create();
        Add(document, NewTable("a", "b"));

        StoryAssembler story = Assemble(document);

        var reader = new SprmReader(story.Paragraphs[2].Properties);
        byte[]? definition = null;
        while (reader.TryRead(out Sprm sprm))
        {
            if (sprm.Opcode == SprmCode.TableDefinition)
                definition = sprm.Operand.ToArray();
        }

        Assert.NotNull(definition);
        Assert.Equal(2, definition[2]);
        Assert.Equal(1 + (3 * 2) + (2 * 20), definition.Length - 2);
    }

    [Fact]
    public void ALineBreak_BecomesTheFormatsBreakCharacter()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("before");
        paragraph.AppendBreak();
        paragraph.AppendText("after");
        Add(document, paragraph);

        Assert.Equal("before\vafter\r", Assemble(document).Text);
    }

    [Fact]
    public void APageBreak_BecomesThePageBreakCharacter()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("before");
        paragraph.AppendBreak(BreakKind.Page);
        paragraph.AppendText("after");
        Add(document, paragraph);

        Assert.Equal("before\fafter\r", Assemble(document).Text);
    }

    [Fact]
    public void TabsSurvive_AndControlCharactersDoNot()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("a\tb\u0003c"));

        Assert.Equal("a\tbc\r", Assemble(document).Text);
    }

    [Fact]
    public void AnEmptyDocument_StillHasAParagraphAndASection()
    {
        StoryAssembler story = Assemble(WordDocument.Create());

        Assert.Equal("\r", story.Text);
        Assert.Single(story.Paragraphs);
        Assert.Single(story.Sections);
    }

    private static StoryAssembler Assemble(WordDocument document)
    {
        var context = new DocWriteContext(document, DocWriteOptions.Default);
        var story = new StoryAssembler(context);
        story.WriteMainStory(document);
        return story;
    }

    private static void Add(WordDocument document, Block block) => document.Sections[0].Blocks.Add(block);

    private static Table NewTable(params string[] cells)
    {
        var table = new Table();
        var row = new TableRow();
        foreach (string text in cells)
        {
            var cell = new TableCell();
            cell.Blocks.Add(new Paragraph(text));
            row.Cells.Add(cell);
        }

        table.Rows.Add(row);
        return table;
    }
}
