using Quillwright.Diagnostics;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

public sealed class DocLoadBudgetTests
{
    [Fact]
    public void ExactInputBoundarySucceeds_AndOneByteLessFails()
    {
        WordDocument source = WordDocument.Create();
        source.Sections[0].AddParagraph("legacy budget");
        byte[] file = DocWriter.Save(source);

        WordDocument loaded = DocReader.LoadWithOptions(file, new DocImportOptions
        {
            Budget = DocumentLoadBudget.Default with { MaxInputBytes = file.LongLength },
        });
        Assert.Equal("legacy budget", loaded.GetText());
        Assert.Equal("legacy budget", DocReader.Load(file, null).GetText());

        DocumentLoadLimitException error = Assert.Throws<DocumentLoadLimitException>(() =>
            DocReader.LoadWithOptions(file, new DocImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxInputBytes = file.LongLength - 1 },
            }));
        Assert.Equal(nameof(DocumentLoadBudget.MaxInputBytes), error.LimitName);
    }

    [Fact]
    public void CompoundDirectoryAndStreamsUseTheCommonBudget()
    {
        byte[] file = DocWriter.Save(WordDocument.Create());

        DocumentLoadLimitException entries = Assert.Throws<DocumentLoadLimitException>(() =>
            DocReader.LoadWithOptions(file, new DocImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxPackageParts = 1 },
            }));
        DocumentLoadLimitException part = Assert.Throws<DocumentLoadLimitException>(() =>
            DocReader.LoadWithOptions(file, new DocImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxPartBytes = 1 },
            }));

        Assert.Equal(nameof(DocumentLoadBudget.MaxPackageParts), entries.LimitName);
        Assert.Equal(nameof(DocumentLoadBudget.MaxPartBytes), part.LimitName);
    }

    [Fact]
    public async Task FileAndStreamEntryPointsStopBeforeUnboundedReadAll()
    {
        byte[] file = DocWriter.Save(WordDocument.Create());
        string path = Path.Combine(Path.GetTempPath(), $"quillwright-budget-{Guid.NewGuid():N}.doc");
        try
        {
            await File.WriteAllBytesAsync(path, file, TestContext.Current.CancellationToken);
            var options = new DocImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxInputBytes = file.LongLength - 1 },
            };

            DocumentLoadLimitException fromFile = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
                await DocReader.LoadWithOptionsAsync(path, options, TestContext.Current.CancellationToken));
            DocumentLoadLimitException fromStream = await Assert.ThrowsAsync<DocumentLoadLimitException>(async () =>
                await DocReader.LoadWithOptionsAsync(
                    new MemoryStream(file), options, TestContext.Current.CancellationToken));

            Assert.Equal(nameof(DocumentLoadBudget.MaxInputBytes), fromFile.LimitName);
            Assert.Equal(nameof(DocumentLoadBudget.MaxInputBytes), fromStream.LimitName);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
