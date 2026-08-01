using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// The two parts that hang off the comments and say more about them than the comments part
/// can: <c>commentsExtensible.xml</c> ([MS-DOCX] 2.10) with the UTC date, the follow-up flag
/// and the reactions, and <c>people.xml</c> ([MS-DOCX] 2.5.3.4) with the identity behind each
/// author name.
/// </summary>
public class CommentMetadataTests
{
    /// <summary>The extension a reaction lives in ([MS-DOCX] 2.2.13).</summary>
    private const string ReactionsExtension =
        "<w16cex:extLst><w16:ext uri=\"{CE6994B0-6A32-4C9F-8C6B-6E91EDA988CE}\">" +
        "<cr:reactions><cr:reaction cr:reactionType=\"heart\">" +
        "<cr:presenceInfo cr:providerId=\"None\" cr:userId=\"Grace\"/>" +
        "</cr:reaction></cr:reactions></w16:ext></w16cex:extLst>";

    [Fact]
    public async Task TheUtcDateAndTheFollowUpFlag_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text here");
        document.Sections[0].Blocks.Add(paragraph);

        Comment comment = document.AddComment(paragraph, 0, 8, "when was this?", "Ada", "A");
        comment.DateUtc = new DateTimeOffset(2024, 3, 17, 9, 41, 0, TimeSpan.Zero);
        comment.IsFollowUp = true;

        Comment reopened = (await ReloadAsync(document)).Comments.Single();

        Assert.Equal(new DateTimeOffset(2024, 3, 17, 9, 41, 0, TimeSpan.Zero), reopened.DateUtc);
        Assert.True(reopened.IsFollowUp);
    }

    /// <summary>
    /// The metadata part names comments by durable identifier and nothing else, so asking for
    /// it means the identifiers part comes too — Word never writes one without the other.
    /// </summary>
    [Fact]
    public async Task TheMetadataPart_BringsTheIdentifiersPartWithIt()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text here");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 0, 8, "dated", "Ada").DateUtc = DateTimeOffset.UtcNow;

        MemoryStream saved = await SaveAsync(document);
        HashSet<string> parts = Names(saved.ToArray());

        Assert.Contains("word/commentsExtensible.xml", parts);
        Assert.Contains("word/commentsIds.xml", parts);
        Assert.Equal(
            Ids(OpenXmlAssert.ReadPart(saved, "commentsIds.xml"), "durableId"),
            Ids(OpenXmlAssert.ReadPart(saved, "commentsExtensible.xml"), "durableId"));
    }

    [Fact]
    public async Task ADocumentOfPlainComments_GrowsNoneOfTheExtraParts()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text here");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 0, 8, "a plain comment", "Ada");

        HashSet<string> parts = Names((await SaveAsync(document)).ToArray());

        Assert.DoesNotContain("word/commentsExtensible.xml", parts);
        Assert.DoesNotContain("word/commentsIds.xml", parts);
        Assert.DoesNotContain("word/people.xml", parts);
    }

    /// <summary>
    /// A reaction is markup this version does not model, so it is carried through verbatim.
    /// The entries around it are rebuilt from the model, which is what drops one left behind
    /// by a comment that is no longer in the document.
    /// </summary>
    [Fact]
    public async Task Reactions_AreCarriedThroughAndStaleEntriesAreDropped()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("alpha beta gamma");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 6, 4, "reacted to", "Ada").DateUtc = DateTimeOffset.UtcNow;

        byte[] original = (await SaveAsync(document)).ToArray();
        byte[] doctored = Rewrite(original, "word/commentsExtensible.xml", part => part
            .Replace("/>", ">" + ReactionsExtension + "</w16cex:commentExtensible>", StringComparison.Ordinal)
            .Replace(
                "</w16cex:commentsExtensible>",
                "<w16cex:commentExtensible w16cex:durableId=\"7FFFFFF0\"/></w16cex:commentsExtensible>",
                StringComparison.Ordinal));

        WordDocument reopened = await LoadAsync(doctored);
        Assert.Single(reopened.Comments);

        MemoryStream resaved = await SaveAsync(reopened);
        string rewritten = OpenXmlAssert.ReadPart(resaved, "commentsExtensible.xml");
        XElement root = XElement.Parse(rewritten);

        Assert.Contains("cr:reaction", rewritten, StringComparison.Ordinal);
        Assert.Contains("heart", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("7FFFFFF0", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.Single(root.Elements());
    }

    /// <summary>
    /// The identities are the one part reconciled by adding rather than rebuilding: a comment
    /// author the part does not name gets an entry, and everything already there stays, because
    /// a name that looks unused may still belong to the author of a tracked change.
    /// </summary>
    [Fact]
    public async Task ANewAuthor_IsAddedToThePeopleAlreadyThere()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("alpha beta gamma");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 0, 5, "the first", "Ada");

        byte[] withPeople = WithPeople((await SaveAsync(document)).ToArray());
        WordDocument reopened = await LoadAsync(withPeople);

        Assert.Equal(["Grace Hopper"], reopened.People.Select(static person => person.Author));
        Assert.Equal("AD", reopened.People[0].ProviderId);

        reopened.AddComment(reopened.Paragraphs.First(), 6, 4, "the second", "Ada");
        WordDocument again = await LoadAsync((await SaveAsync(reopened)).ToArray());

        Assert.Equal(["Grace Hopper", "Ada"], again.People.Select(static person => person.Author));
        Assert.Equal("None", again.People[1].ProviderId);
        Assert.Equal("Ada", again.People[1].UserId);
    }

    /// <summary>A durable identifier is public, and the same one comes back after a save.</summary>
    [Fact]
    public async Task ADurableIdentifier_IsMintedOnceAndKept()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text here");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 0, 8, "dated", "Ada").DateUtc = DateTimeOffset.UtcNow;

        WordDocument reopened = await ReloadAsync(document);
        string? minted = reopened.Comments.Single().DurableId;

        Assert.NotNull(minted);
        Assert.Equal(minted, (await ReloadAsync(reopened)).Comments.Single().DurableId);
    }

    /// <summary>Gives a package a people part naming somebody who left no comment.</summary>
    private static byte[] WithPeople(byte[] package) => WithPart(
        package,
        "word/people.xml",
        "http://schemas.microsoft.com/office/2011/relationships/people",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w15:people xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml"><w15:person w15:author="Grace Hopper"><w15:presenceInfo w15:providerId="AD" w15:userId="S::grace@example.com::1"/></w15:person></w15:people>
        """);

    private static async Task<MemoryStream> SaveAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "comment metadata");
        return buffer;
    }

    private static async Task<WordDocument> ReloadAsync(WordDocument document) =>
        await LoadAsync((await SaveAsync(document)).ToArray());

    private static ValueTask<WordDocument> LoadAsync(byte[] package) =>
        WordDocument.LoadAsync(new MemoryStream(package), cancellationToken: TestContext.Current.CancellationToken);

    private static HashSet<string> Names(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return [.. archive.Entries.Select(static entry => entry.FullName)];
    }

    private static List<string> Ids(string part, string attribute) =>
        [.. System.Text.RegularExpressions.Regex.Matches(part, attribute + "=\"([^\"]+)\"")
            .Select(static match => match.Groups[1].Value)];

    /// <summary>Rebuilds a package with one of its parts put through a transformation.</summary>
    private static byte[] Rewrite(byte[] package, string path, Func<string, string> change) =>
        Build(Read(package), entries => entries[path] = change(entries[path]));

    /// <summary>
    /// Adds a part to a package the way a producer would: the bytes, the relationship from the
    /// main part, and the content type that makes it openable.
    /// </summary>
    private static byte[] WithPart(byte[] package, string path, string relationshipType, string contentType, string content) =>
        Build(Read(package), entries =>
        {
            entries[path] = content;
            entries["[Content_Types].xml"] = entries["[Content_Types].xml"].Replace(
                "</Types>",
                $"<Override PartName=\"/{path}\" ContentType=\"{contentType}\"/></Types>",
                StringComparison.Ordinal);
            entries["word/_rels/document.xml.rels"] = entries["word/_rels/document.xml.rels"].Replace(
                "</Relationships>",
                $"<Relationship Id=\"rIdAdded\" Type=\"{relationshipType}\" " +
                $"Target=\"{path["word/".Length..]}\"/></Relationships>",
                StringComparison.Ordinal);
        });

    private static Dictionary<string, string> Read(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            entries[entry.FullName] = reader.ReadToEnd();
        }

        return entries;
    }

    private static byte[] Build(Dictionary<string, string> entries, Action<Dictionary<string, string>> change)
    {
        change(entries);

        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                using Stream writing = archive.CreateEntry(name).Open();
                writing.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return output.ToArray();
    }
}
