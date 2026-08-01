using System.IO.Compression;
using System.Text;
using Quillwright.Editing;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Tests;

/// <summary>
/// Covers <c>mc:AlternateContent</c> inside a run: the branch selection of ISO/IEC 29500-3
/// §9.3, and the promise that resolving a branch costs the alternatives nothing.
/// </summary>
public class MarkupCompatibilityTests
{
    /// <summary>
    /// A fallback with no words in it, so that these tests are about branch selection alone
    /// and not about the text box the shape reader would otherwise find.
    /// </summary>
    private const string VmlFallback =
        "<mc:Fallback><w:pict><v:rect id=\"legacy\" style=\"width:85pt;height:57pt\">" +
        "<v:fill color=\"#eeeeee\"/></v:rect></w:pict></mc:Fallback>";

    [Fact]
    public async Task AWrappedDrawing_IsReadAsAPicture()
    {
        using MemoryStream package = await WrappedPictureAsync("wps");
        WordDocument document = await LoadAsync(package);

        AlternateContent wrapper = Wrapper(document);
        Picture picture = Assert.IsType<Picture>(wrapper.Content);

        Assert.Equal("image/png", picture.Image.ContentType);
        Assert.Contains("<mc:Choice", wrapper.Prefix, StringComparison.Ordinal);
        Assert.Contains("<mc:Fallback>", wrapper.Suffix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWrappedDrawing_LeftAlone_IsSavedByteForByte()
    {
        using MemoryStream package = await WrappedPictureAsync("wps");
        using MemoryStream once = await DocumentFixture.SaveAsync(await LoadAsync(package));

        once.Position = 0;
        using MemoryStream twice = await DocumentFixture.SaveAsync(await LoadAsync(once));

        Assert.Contains("<v:rect", Block(ReadDocumentPart(once)), StringComparison.Ordinal);
        Assert.Equal(Block(ReadDocumentPart(once)), Block(ReadDocumentPart(twice)));
        OpenXmlAssert.Valid(twice, "a resolved compatibility block");
    }

    [Fact]
    public async Task ResizingAWrappedPicture_ChangesTheChoiceAndNotTheFallback()
    {
        using MemoryStream package = await WrappedPictureAsync("wps");
        using MemoryStream untouched = await DocumentFixture.SaveAsync(await LoadAsync(package));

        untouched.Position = 0;
        WordDocument document = await LoadAsync(untouched);
        var picture = (Picture)Wrapper(document).Content;
        picture.Width = Length.FromCentimeters(7);
        picture.Height = Length.FromCentimeters(5);

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        string block = Block(ReadDocumentPart(saved));

        Assert.Contains($"cx=\"{Length.FromCentimeters(7).Emu}\"", block, StringComparison.Ordinal);
        Assert.Equal(Fallback(Block(ReadDocumentPart(untouched))), Fallback(block));

        saved.Position = 0;
        var reloaded = (Picture)Wrapper(await LoadAsync(saved)).Content;
        Assert.Equal(Length.FromCentimeters(7).Emu, reloaded.Width.Emu);
    }

    [Fact]
    public async Task AChoiceRequiringAnUnknownVocabulary_LeavesTheFallbackSelected()
    {
        // The prefix resolves to a namespace no OOXML reader knows, so the drawing in the
        // Choice is not the branch a reader renders and must not be the one modelled.
        using MemoryStream package = await WrappedPictureAsync("qw");
        WordDocument document = await LoadAsync(package);

        Assert.Empty(Objects(document).OfType<AlternateContent>());
        Assert.Single(Objects(document).OfType<RawInline>());
    }

    /// <summary>
    /// The same selection at block level, where a branch holds whole paragraphs rather than one
    /// drawing. Word wraps a paragraph this way whenever it holds something an older reader
    /// would not understand, and preserving the whole thing left the words unreachable.
    /// </summary>
    [Fact]
    public async Task ABlockLevelChoice_IsModelledAsTheBranchAReaderShows()
    {
        using MemoryStream package = await WrappedParagraphAsync("wps");
        WordDocument document = await LoadAsync(package);

        var wrapper = Assert.IsType<AlternateContentBlock>(document.Sections[0].Blocks[1]);
        Paragraph paragraph = Assert.Single(wrapper.Blocks.Paragraphs);

        Assert.Equal("The modern branch.", paragraph.Text);
        Assert.Contains("<mc:Choice", wrapper.Prefix, StringComparison.Ordinal);
        Assert.Contains("<mc:Fallback>", wrapper.Suffix, StringComparison.Ordinal);
        Assert.Contains("The modern branch.", document.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlockLevelChoiceRequiringAnUnknownVocabulary_TakesTheFallback()
    {
        using MemoryStream package = await WrappedParagraphAsync("qw");
        WordDocument document = await LoadAsync(package);

        var wrapper = Assert.IsType<AlternateContentBlock>(document.Sections[0].Blocks[1]);

        Assert.Equal("The older branch.", Assert.Single(wrapper.Blocks.Paragraphs).Text);
    }

    [Fact]
    public async Task AChoiceRequiringAnOfficialLookingButUnsupportedVocabulary_TakesTheFallback()
    {
        using MemoryStream package = await WrappedParagraphAsync("future");
        WordDocument document = await LoadAsync(package);

        var wrapper = Assert.IsType<AlternateContentBlock>(document.Sections[0].Blocks[1]);

        Assert.Equal("The older branch.", Assert.Single(wrapper.Blocks.Paragraphs).Text);
    }

    [Fact]
    public async Task AnIgnorableExtensionBesideTheBranches_DoesNotPreventSelection()
    {
        using MemoryStream wrapped = await WrappedPictureAsync("wps");
        using MemoryStream package = Rewrite(wrapped, static xml => xml.Replace(
            "<mc:AlternateContent xmlns:qw=\"urn:quillwright:not-an-ooxml-vocabulary\">",
            "<mc:AlternateContent xmlns:qw=\"urn:quillwright:not-an-ooxml-vocabulary\" " +
            "mc:Ignorable=\"qw\"><qw:metadata/>",
            StringComparison.Ordinal));

        AlternateContent wrapper = Wrapper(await LoadAsync(package));

        Assert.IsType<Picture>(wrapper.Content);
    }

    [Fact]
    public async Task AnIgnorableExtensionInsideTheSelectedBranch_DoesNotHideItsDrawing()
    {
        using MemoryStream wrapped = await WrappedPictureAsync("wps");
        using MemoryStream package = Rewrite(wrapped, static xml => xml.Replace(
            "<mc:Choice Requires=\"wps\">",
            "<mc:Choice Requires=\"wps\" mc:Ignorable=\"qw\"><qw:metadata/>",
            StringComparison.Ordinal));

        AlternateContent wrapper = Wrapper(await LoadAsync(package));

        Assert.IsType<Picture>(wrapper.Content);
    }

    [Fact]
    public async Task ABlockLevelChoice_LeftAlone_IsSavedByteForByte()
    {
        using MemoryStream package = await WrappedParagraphAsync("wps");
        using MemoryStream once = await DocumentFixture.SaveAsync(await LoadAsync(package));

        once.Position = 0;
        using MemoryStream twice = await DocumentFixture.SaveAsync(await LoadAsync(once));

        Assert.Contains("The older branch.", Block(ReadDocumentPart(once)), StringComparison.Ordinal);
        Assert.Equal(Block(ReadDocumentPart(once)), Block(ReadDocumentPart(twice)));
        OpenXmlAssert.Valid(twice, "a resolved block-level compatibility block");
    }

    /// <summary>An edit reaches the selected branch and leaves every other branch as it was.</summary>
    [Fact]
    public async Task EditingTheSelectedBranch_LeavesTheFallbackAlone()
    {
        using MemoryStream package = await WrappedParagraphAsync("wps");
        using MemoryStream untouched = await DocumentFixture.SaveAsync(await LoadAsync(package));

        untouched.Position = 0;
        WordDocument document = await LoadAsync(untouched);
        document.Replace("The modern branch.", "Edited.");

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        string block = Block(ReadDocumentPart(saved));

        Assert.Contains("Edited.", block, StringComparison.Ordinal);
        Assert.Equal(Fallback(Block(ReadDocumentPart(untouched))), Fallback(block));
    }

    private static ValueTask<WordDocument> LoadAsync(Stream package) =>
        WordDocument.LoadAsync(package, cancellationToken: TestContext.Current.CancellationToken);

    private static AlternateContent Wrapper(WordDocument document) =>
        Objects(document).OfType<AlternateContent>().Single();

    private static IEnumerable<InlineObject> Objects(WordDocument document) =>
        document.Paragraphs.SelectMany(static p => p.Objects).Select(static o => o.Object);

    /// <summary>The compatibility block on its own, so a comparison is not about the rest of the part.</summary>
    private static string Block(string documentXml) => Between(documentXml, "<mc:AlternateContent", "</mc:AlternateContent>");

    /// <summary>The branch an older reader falls back to, which no edit here may touch.</summary>
    private static string Fallback(string block) => Between(block, "<mc:Fallback>", "</mc:Fallback>");

    private static string Between(string markup, string open, string close)
    {
        int start = markup.IndexOf(open, StringComparison.Ordinal);
        int end = markup.IndexOf(close, StringComparison.Ordinal);
        Assert.InRange(start, 0, end);
        return markup[start..(end + close.Length)];
    }

    /// <summary>
    /// A package holding one picture wrapped the way Word wraps a modern drawing: the
    /// drawing in a Choice, a VML rendering in the Fallback.
    /// </summary>
    private static async Task<MemoryStream> WrappedPictureAsync(string requires)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendPicture(
            ImageData.FromBytes(TestImages.Png), Length.FromCentimeters(3), Length.FromCentimeters(2));

        using MemoryStream plain = await DocumentFixture.SaveAsync(document);
        return Rewrite(plain, xml => Wrap(xml, requires));
    }

    /// <summary>
    /// A package whose second paragraph is wrapped at block level: the modern wording in a
    /// Choice, an older wording in the Fallback.
    /// </summary>
    private static async Task<MemoryStream> WrappedParagraphAsync(string requires)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Before.");
        document.Sections[0].AddParagraph("The modern branch.");

        using MemoryStream plain = await DocumentFixture.SaveAsync(document);
        return Rewrite(plain, xml => WrapBlock(xml, requires));
    }

    private static string WrapBlock(string documentXml, string requires)
    {
        const string Marker = "<w:p><w:r><w:t xml:space=\"preserve\">The modern branch.</w:t></w:r></w:p>";
        int start = documentXml.IndexOf(Marker, StringComparison.Ordinal);
        Assert.InRange(start, 0, documentXml.Length);

        string wrapper =
            "<mc:AlternateContent xmlns:qw=\"urn:quillwright:not-an-ooxml-vocabulary\" " +
            "xmlns:future=\"http://schemas.openxmlformats.org/officeDocument/2099/future\">" +
            $"<mc:Choice Requires=\"{requires}\">{Marker}</mc:Choice>" +
            "<mc:Fallback><w:p><w:r><w:t>The older branch.</w:t></w:r></w:p></mc:Fallback>" +
            "</mc:AlternateContent>";

        return documentXml[..start] + wrapper + documentXml[(start + Marker.Length)..];
    }

    private static string Wrap(string documentXml, string requires)
    {
        int start = documentXml.IndexOf("<w:drawing>", StringComparison.Ordinal);
        int end = documentXml.IndexOf("</w:drawing>", StringComparison.Ordinal) + "</w:drawing>".Length;
        Assert.InRange(start, 0, end);

        string drawing = documentXml[start..end];
        string wrapper =
            $"<mc:AlternateContent xmlns:qw=\"urn:quillwright:not-an-ooxml-vocabulary\">" +
            $"<mc:Choice Requires=\"{requires}\">{drawing}</mc:Choice>{VmlFallback}</mc:AlternateContent>";
        return documentXml[..start] + wrapper + documentXml[end..];
    }

    private static string ReadDocumentPart(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        return reader.ReadToEnd();
    }

    /// <summary>Copies a package through, replacing the markup of the main document part.</summary>
    private static MemoryStream Rewrite(MemoryStream package, Func<string, string> edit)
    {
        var result = new MemoryStream();
        package.Position = 0;
        using (var source = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                using Stream from = entry.Open();
                using Stream to = target.CreateEntry(entry.FullName).Open();
                if (entry.FullName != "word/document.xml")
                {
                    from.CopyTo(to);
                    continue;
                }

                using var reader = new StreamReader(from);
                byte[] edited = Encoding.UTF8.GetBytes(edit(reader.ReadToEnd()));
                to.Write(edited, 0, edited.Length);
            }
        }

        result.Position = 0;
        return result;
    }
}
