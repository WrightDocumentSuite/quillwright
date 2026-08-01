using Quillwright.Model;
using Quillwright.Streaming;
using Quillwright.Styles;

namespace Quillwright.Tests;

public class StreamingTests
{
    [Fact]
    public async Task StreamingWriter_ProducesAValidDocument()
    {
        var buffer = new MemoryStream();
        DocxWriter writer = await DocxWriter.CreateAsync(buffer, TestContext.Current.CancellationToken);
        await using (writer)
        {
            writer.Styles.GetOrAdd("Heading1");
            writer.WriteParagraph("Streamed report", styleId: "Heading1");
            for (int i = 0; i < 500; i++)
            {
                writer.WriteParagraph($"Line {i}", RunFormat.Default with { Bold = i % 2 == 0 });
                await writer.FlushIfNeededAsync(TestContext.Current.CancellationToken);
            }
        }

        buffer.Position = 0;
        OpenXmlAssert.Valid(buffer, "a streamed document");

        buffer.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(501, reloaded.Paragraphs.Count());
        Assert.Equal("Streamed report", reloaded.Paragraphs.First().Text);
        Assert.Equal("Line 499", reloaded.Paragraphs.Last().Text);
    }

    [Fact]
    public async Task StreamingWriter_WritesTablesAndPictures()
    {
        var buffer = new MemoryStream();
        DocxWriter writer = await DocxWriter.CreateAsync(buffer, TestContext.Current.CancellationToken);
        await using (writer)
        {
            Table table = Table.Create(2, 2);
            table[0, 0].SetText("A");
            writer.WriteTable(table);

            var paragraph = new Paragraph();
            paragraph.AppendPicture(ImageData.FromBytes(TestImages.Png));
            writer.WriteParagraph(paragraph);
        }

        buffer.Position = 0;
        OpenXmlAssert.Valid(buffer, "a streamed table and picture");

        buffer.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(reloaded.Sections[0].Tables);
        Assert.Single(reloaded.Media);
    }

    [Fact]
    public async Task StreamingReader_YieldsBlocksWithoutBuildingTheDocument()
    {
        WordDocument source = WordDocument.Create();
        for (int i = 0; i < 50; i++)
            source.Sections[0].AddParagraph($"paragraph {i}");
        source.Sections[0].AddTable(2, 2)[0, 0].SetText("cell");

        using MemoryStream package = await DocumentFixture.SaveAsync(source);

        DocxReader reader = await DocxReader.OpenAsync(package, TestContext.Current.CancellationToken);
        await using (reader)
        {
            var blocks = new List<Block>();
            await foreach (Block block in reader.ReadBlocksAsync(TestContext.Current.CancellationToken))
                blocks.Add(block);

            Assert.Equal(51, blocks.Count);
            Assert.Equal("paragraph 0", Assert.IsType<Paragraph>(blocks[0]).Text);
            Assert.Equal("cell", Assert.IsType<Table>(blocks[^1])[0, 0].GetText());
        }
    }

    [Fact]
    public async Task StreamingReader_ExtractsTextWithoutTheDom()
    {
        WordDocument source = WordDocument.Create();
        source.Sections[0].AddParagraph("first");
        source.Sections[0].AddParagraph("second");
        using MemoryStream package = await DocumentFixture.SaveAsync(source);

        DocxReader reader = await DocxReader.OpenAsync(package, TestContext.Current.CancellationToken);
        await using (reader)
        {
            var lines = new List<string>();
            await foreach (string line in reader.ReadTextAsync(TestContext.Current.CancellationToken))
                lines.Add(line);

            Assert.Equal(["first", "second"], lines);
        }
    }
}
