using System.Text;
using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests.Markdown;

public class MarkdownMediaAndNoteTests
{
    [Fact]
    public void Pictures_AreDeduplicatedByBytesInFirstReferenceOrder()
    {
        WordDocument document = WordDocument.Create();
        ImageData first = ImageData.FromBytes(TestImages.Png);
        ImageData sameBytes = ImageData.FromBytes(TestImages.Png.ToArray());
        Picture picture = document.Sections[0].AddParagraph().AppendPicture(first);
        picture.Description = "tiny logo";
        document.Sections[0].AddParagraph().AppendPicture(sameBytes);

        MarkdownDocument markdown = document.ToMarkdown(new MarkdownExportOptions
        {
            MediaDirectoryName = "assets/pictures here",
        });

        Assert.Equal(
            "![tiny logo](assets/pictures%20here/image1.png)\n\n" +
            "![image1.png](assets/pictures%20here/image1.png)\n",
            markdown.Text);
        MarkdownImage image = Assert.Single(markdown.Images);
        Assert.Equal("image1.png", image.FileName);
        Assert.Equal("image/png", image.ContentType);
        Assert.True(image.Content.Span.SequenceEqual(TestImages.Png));
    }

    [Fact]
    public void ResizedPicture_UsesHtmlOnlyWhenDimensionPreservationIsEnabled()
    {
        WordDocument document = WordDocument.Create();
        Picture picture = document.Sections[0].AddParagraph().AppendPicture(
            ImageData.FromBytes(TestImages.Png), Length.FromPixels(100), Length.FromPixels(50));
        picture.Name = "preview";

        MarkdownDocument preserved = document.ToMarkdown();
        MarkdownDocument plain = document.ToMarkdown(new MarkdownExportOptions
        {
            PreserveImageDimensions = false,
        });

        Assert.Equal("<img src=\"media/image1.png\" alt=\"preview\" width=\"100\" height=\"50\">\n",
            preserved.Text);
        Assert.Equal("![preview](media/image1.png)\n", plain.Text);
        Assert.Contains(preserved.Diagnostics,
            warning => warning.Subject == "image-dimensions");
    }

    [Fact]
    public void HiddenOrDisabledPictures_DoNotCreateOrphanedMedia()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendPicture(
            ImageData.FromBytes(TestImages.Png), format: RunFormat.Default with { Hidden = true });

        MarkdownDocument hidden = document.ToMarkdown();
        MarkdownDocument disabled = document.ToMarkdown(new MarkdownExportOptions
        {
            IncludePictures = false,
            IncludeHiddenText = true,
        });

        Assert.Equal("\n", hidden.Text);
        Assert.Equal("\n", disabled.Text);
        Assert.Empty(hidden.Images);
        Assert.Empty(disabled.Images);
    }

    [Fact]
    public void BrowserUnfriendlyImage_IsPreservedAndDiagnosed()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendPicture(
            ImageData.FromBytes(new byte[] { 1, 2, 3, 4 }, "image/tiff"),
            Length.FromPixels(10), Length.FromPixels(10));

        MarkdownDocument markdown = document.ToMarkdown(new MarkdownExportOptions
        {
            PreserveImageDimensions = false,
        });

        Assert.Single(markdown.Images);
        Assert.Equal("image1.tiff", markdown.Images[0].FileName);
        Assert.Contains(markdown.Diagnostics,
            warning => warning.Kind == MarkdownExportWarningKind.MediaMayNotRender &&
                       warning.Subject == "tiff");
    }

    [Fact]
    public void GitHubNotes_AreDefinedOnceInFirstReferenceOrder()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Body");
        Note footnote = document.AddFootnote(paragraph, "Footnote text.");
        document.AddEndnote(paragraph, "Endnote text.");
        paragraph.AppendObject(new NoteReference { Id = footnote.Id });

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal(
            "Body[^fn-1][^en-1][^fn-1]\n\n" +
            "[^fn-1]: Footnote text.\n\n" +
            "[^en-1]: Endnote text.\n",
            markdown.Text);
        Assert.Equal(1, Count(markdown.Text, "[^fn-1]:"));
    }

    [Fact]
    public void CommonMarkNotes_UseHtmlWithoutGitHubFootnoteSyntax()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Body");
        document.AddFootnote(paragraph, "<safe> & sound");

        MarkdownDocument markdown = document.ToMarkdown(new MarkdownExportOptions
        {
            Flavor = MarkdownFlavor.CommonMark,
        });

        Assert.Contains("Body<sup><a href=\"#fn-1\">1</a></sup>", markdown.Text,
            StringComparison.Ordinal);
        Assert.Contains("<section class=\"footnotes\">", markdown.Text, StringComparison.Ordinal);
        Assert.Contains("<li id=\"fn-1\"><p>&lt;safe&gt; &amp; sound</p></li>", markdown.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[^", markdown.Text, StringComparison.Ordinal);
        Assert.Contains(markdown.Diagnostics,
            warning => warning.Subject == "commonmark-notes");
    }

    [Fact]
    public void MissingNoteTarget_RemainsVisibleAndIsDiagnosed()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Body").AppendObject(new NoteReference { Id = 404 });

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("Body<sup>?</sup>\n", markdown.Text);
        Assert.Contains(markdown.Diagnostics,
            warning => warning.Subject == "missing-footnote");
    }

    [Fact]
    public async Task SaveAsync_WritesUtf8WithoutBomAndLeavesUnownedFilesAlone()
    {
        string root = Path.Combine(Path.GetTempPath(), "quillwright-markdown-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "assets", "pictures here"));
            await File.WriteAllTextAsync(Path.Combine(root, "keep.txt"), "keep",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "assets", "pictures here", "stale.bin"), "stale",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, MarkdownDocument.DefaultFileName), "old",
                TestContext.Current.CancellationToken);

            WordDocument document = WordDocument.Create();
            document.Sections[0].AddParagraph("Привет").AppendPicture(ImageData.FromBytes(TestImages.Png));
            var options = new MarkdownExportOptions { MediaDirectoryName = "assets/pictures here" };

            await document.ExportMarkdownAsync(root, options, TestContext.Current.CancellationToken);

            byte[] markdown = await File.ReadAllBytesAsync(
                Path.Combine(root, MarkdownDocument.DefaultFileName), TestContext.Current.CancellationToken);
            Assert.False(markdown.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.Equal("Привет![image1.png](assets/pictures%20here/image1.png)\n",
                Encoding.UTF8.GetString(markdown));
            Assert.Equal(TestImages.Png, await File.ReadAllBytesAsync(
                Path.Combine(root, "assets", "pictures here", "image1.png"),
                TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(root, "keep.txt")));
            Assert.True(File.Exists(Path.Combine(root, "assets", "pictures here", "stale.bin")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static int Count(string value, string substring) =>
        (value.Length - value.Replace(substring, string.Empty, StringComparison.Ordinal).Length) /
        substring.Length;
}
