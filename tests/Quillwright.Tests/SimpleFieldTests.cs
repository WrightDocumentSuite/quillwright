using System.Globalization;
using System.IO.Compression;
using System.Text;
using Quillwright.Editing;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// A field written as one element (<c>w:fldSimple</c>, ISO/IEC 29500-1 §17.16.19). It says
/// the same thing as the five-character sequence, and Word writes whichever it feels like, so
/// a caller asking for the fields of a document has to be handed both.
/// </summary>
public class SimpleFieldTests
{
    private static readonly FieldUpdateOptions Invariant = new() { Culture = CultureInfo.InvariantCulture };

    [Fact]
    public async Task ASimpleField_IsOneOfTheDocumentsFields()
    {
        WordDocument document = await LoadAsync("AUTHOR", "someone");

        Field field = Assert.Single(document.Fields());
        Assert.True(field.IsSimple);
        Assert.Equal("AUTHOR", field.Name);
        Assert.Equal("someone", field.Result);
    }

    [Fact]
    public async Task ASimpleField_IsUpdatedLikeAnyOther()
    {
        WordDocument document = await LoadAsync("AUTHOR", "someone");
        document.Properties.Creator = "Ada Lovelace";

        Assert.Equal(1, document.UpdateFields(Invariant));
        Assert.Equal("Ada Lovelace", document.Fields().Single().Result);
    }

    [Fact]
    public async Task ASimpleFieldThatNeedsALayout_IsMarkedDirty()
    {
        WordDocument document = await LoadAsync("PAGE", "1");

        Assert.Equal(0, document.UpdateFields(Invariant));
        Assert.True(document.Fields().Single().IsDirty);
    }

    [Fact]
    public async Task ASimpleField_KeepsItsFormAcrossARoundTrip()
    {
        WordDocument document = await LoadAsync("AUTHOR", "someone");

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        OpenXmlAssert.Valid(saved, "a simple field");
        string markup = OpenXmlAssert.ReadPart(saved, "document.xml");

        Assert.Contains("<w:fldSimple w:instr=\"AUTHOR\">", markup, StringComparison.Ordinal);
        Assert.Contains("someone", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUpdatedSimpleField_IsNoLongerDirty()
    {
        WordDocument document = await LoadAsync("AUTHOR", "someone", dirty: true);
        document.Properties.Creator = "Ada";

        Assert.Equal(1, document.UpdateFields(Invariant));

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        Assert.DoesNotContain("w:dirty", OpenXmlAssert.ReadPart(saved, "document.xml"), StringComparison.Ordinal);
    }

    /// <summary>Both forms in one paragraph come back in the order they appear.</summary>
    [Fact]
    public async Task BothFormsTogether_ComeBackInDocumentOrder()
    {
        WordDocument document = await LoadAsync("AUTHOR", "someone");
        Paragraph paragraph = document.Sections[0].Blocks.Paragraphs.First();
        paragraph.AppendText(" and ");
        paragraph.AppendField("TITLE", "untitled");

        Assert.Equal([true, false], document.Fields().Select(static f => f.IsSimple));
    }

    /// <summary>Builds a package whose one paragraph holds a field in the single-element form.</summary>
    private static async Task<WordDocument> LoadAsync(string instruction, string result, bool dirty = false)
    {
        const string Placeholder = "QW-FIELD";
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph(Placeholder);

        using MemoryStream plain = await DocumentFixture.SaveAsync(document);
        string field =
            $"<w:fldSimple w:instr=\"{instruction}\"{(dirty ? " w:dirty=\"1\"" : string.Empty)}>" +
            $"<w:r><w:t>{result}</w:t></w:r></w:fldSimple>";

        using MemoryStream package = Rewrite(plain, xml => xml.Replace(
            $"<w:r><w:t xml:space=\"preserve\">{Placeholder}</w:t></w:r>", field, StringComparison.Ordinal));

        package.Position = 0;
        return await WordDocument.LoadAsync(package, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static MemoryStream Rewrite(MemoryStream package, Func<string, string> edit)
    {
        package.Position = 0;
        var result = new MemoryStream();
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
