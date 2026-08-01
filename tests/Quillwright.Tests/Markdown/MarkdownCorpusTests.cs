using Quillwright.Diagnostics;
using Quillwright.Markdown;
using Quillwright.Model;

namespace Quillwright.Tests.Markdown;

public class MarkdownCorpusTests
{
    private static readonly string[] CorpusRoots = ReferenceCorpus.Roots;

    [Fact]
    public async Task RepresentativeRealDocuments_RenderDeterministicallyWithoutOrphanedMedia()
    {
        string[] paths =
        [
            .. CorpusRoots.Where(Directory.Exists)
                .SelectMany(root => Directory.EnumerateFiles(root, "*.docx", SearchOption.AllDirectories))
                .Where(path => new FileInfo(path).Length is > 0 and < 2 * 1024 * 1024)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(16),
        ];

        foreach (string path in paths)
        {
            WordDocument document;
            try
            {
                document = await WordDocument.LoadAsync(
                    path, cancellationToken: TestContext.Current.CancellationToken);
            }
            catch (DocxFormatException)
            {
                continue;
            }

            string sourceText = document.GetText();
            MarkdownDocument first = document.ToMarkdown();
            MarkdownDocument second = document.ToMarkdown();

            Assert.Equal(first.Text, second.Text);
            Assert.Equal(first.Images.Select(image => image.FileName),
                second.Images.Select(image => image.FileName));
            Assert.Equal(sourceText, document.GetText());
            Assert.DoesNotContain('\r', first.Text);
            Assert.EndsWith("\n", first.Text, StringComparison.Ordinal);
            Assert.False(first.Text.EndsWith("\n\n", StringComparison.Ordinal));
            foreach (MarkdownImage image in first.Images)
                Assert.Contains(image.FileName, first.Text, StringComparison.Ordinal);
        }
    }
}
