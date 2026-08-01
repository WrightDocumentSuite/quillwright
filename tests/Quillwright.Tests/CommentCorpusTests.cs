using System.IO.Compression;
using System.Text;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Puts every commented document the reference repositories ship with through a save and back,
/// and requires the comments to come out where they went in.
/// </summary>
/// <remarks>
/// <para>
/// A comment is spread over three places that have to agree — the marks around the text, the
/// reference character, and the body in a part of its own — and threading adds a fourth that
/// names comments by a paragraph identifier rather than by their id. Nothing about that is
/// visible in the document's text, so the general corpus test walks straight past it.
/// </para>
/// <para>
/// The documents are worth more than anything written here: they cover a comment reference
/// before its range, one between the two marks, ranges that overlap without nesting, a range
/// spanning two paragraphs, comments on pictures and inside tables, ids out of order, and
/// conversations several replies deep.
/// </para>
/// </remarks>
public class CommentCorpusTests
{
    private static readonly string[] Roots =
    [
        ReferenceCorpus.TelerikPath("Flow/Tests/Flow/TestDocuments/Comments"),
        ReferenceCorpus.OpenXmlPath(
            "test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/O15Conformance/WD/" +
            "CommentExTest/Comments-Sample-15-12-01"),
    ];

    public static TheoryData<string> Documents
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in Paths())
                data.Add(path);

            if (data.Count == 0)
                data.Add(string.Empty);
            return data;
        }
    }

    private static List<string> Paths() =>
    [
        .. Roots
            .Where(Directory.Exists)
            .SelectMany(static root => Directory.EnumerateFiles(root, "*.doc?"))
            .Order(StringComparer.Ordinal),
    ];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task Comments_ComeBackWhereTheyWent(string path)
    {
        Assert.SkipWhen(path.Length == 0, ReferenceCorpus.Absent);

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        WordDocument document = await WordDocument.LoadAsync(path, cancellationToken: cancellationToken);
        string before = Fingerprint(document);

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: cancellationToken);
        buffer.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(buffer, cancellationToken: cancellationToken);

        Assert.Equal(before, Fingerprint(reloaded));
    }

    /// <summary>
    /// A document that carried durable identifiers keeps them, and a comment added to it gets
    /// one of its own. They are what tells two people editing at once that they are looking at
    /// the same comment, which a comment id cannot do because it is only an index in one save.
    /// </summary>
    [Fact]
    public async Task DurableIdentifiers_SurviveAndAreMintedForNewComments()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        List<string> paths = [.. Paths().Where(HasDurableIds)];
        Assert.SkipWhen(paths.Count == 0, "No document in the reference corpus carries durable identifiers.");

        foreach (string path in paths)
        {
            WordDocument document = await WordDocument.LoadAsync(path, cancellationToken: cancellationToken);
            List<string?> before = [.. document.Comments.Select(static c => c.DurableId)];
            Assert.NotEmpty(before);
            Assert.DoesNotContain(null, before);

            document.AddComment(document.Paragraphs.First(static p => p.TextLength > 0), 0, 1, "added", "Tester");

            var buffer = new MemoryStream();
            await document.SaveAsync(buffer, cancellationToken: cancellationToken);
            Assert.Contains("word/commentsIds.xml", Parts(buffer.ToArray()));

            buffer.Position = 0;
            WordDocument reloaded = await WordDocument.LoadAsync(buffer, cancellationToken: cancellationToken);
            List<string?> after = [.. reloaded.Comments.Select(static c => c.DurableId)];

            Assert.Equal(before, after[..before.Count]);
            Assert.DoesNotContain(null, after);
            Assert.Equal(after.Count, after.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    /// <summary>
    /// The UTC timestamp Word 2018 added ([MS-DOCX] 2.10) is a second date beside the one on
    /// the comment itself, and the only one whose time zone is defined. It has to survive, and
    /// so does everything in the extension list beside it that this version does not model.
    /// </summary>
    [Fact]
    public async Task TheUtcDates_AreReadAndWrittenBack()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        List<string> paths = [.. Paths().Where(HasExtendedMetadata)];
        Assert.SkipWhen(paths.Count == 0, "No document in the reference corpus carries the extended metadata.");

        foreach (string path in paths)
        {
            WordDocument document = await WordDocument.LoadAsync(path, cancellationToken: cancellationToken);
            List<DateTimeOffset?> before = [.. document.Comments.Select(static c => c.DateUtc)];

            Assert.NotEmpty(before);
            Assert.DoesNotContain(null, before);

            // Word writes the same instant twice: as a local wall clock in the comments part
            // and as the real UTC here, so the two are the same date but not the same time.
            Assert.All(document.Comments, static comment => Assert.NotNull(comment.Date));

            var buffer = new MemoryStream();
            await document.SaveAsync(buffer, cancellationToken: cancellationToken);
            Assert.Contains("word/commentsExtensible.xml", Parts(buffer.ToArray()));

            buffer.Position = 0;
            WordDocument reloaded = await WordDocument.LoadAsync(buffer, cancellationToken: cancellationToken);
            Assert.Equal(before, [.. reloaded.Comments.Select(static c => c.DateUtc)]);
        }
    }

    /// <summary>
    /// The identities behind the author names come back with the document and are written out
    /// again, with an entry added for an author the part had never heard of.
    /// </summary>
    [Fact]
    public async Task ThePeople_AreReadAndKeptWhenACommentIsAdded()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        List<string> paths = [.. Paths().Where(static path => Has(path, "word/people.xml"))];
        Assert.SkipWhen(paths.Count == 0, "No document in the reference corpus carries the people part.");

        foreach (string path in paths)
        {
            WordDocument document = await WordDocument.LoadAsync(path, cancellationToken: cancellationToken);
            List<string> before = [.. document.People.Select(static person => person.Author)];

            document.AddComment(document.Paragraphs.First(static p => p.TextLength > 0), 0, 1, "added", "A Newcomer");

            var buffer = new MemoryStream();
            await document.SaveAsync(buffer, cancellationToken: cancellationToken);
            buffer.Position = 0;
            WordDocument reloaded = await WordDocument.LoadAsync(buffer, cancellationToken: cancellationToken);

            List<string> after = [.. reloaded.People.Select(static person => person.Author)];
            Assert.Equal(before, after[..before.Count]);
            Assert.Contains("A Newcomer", after);
        }
    }

    private static bool HasDurableIds(string path) => Has(path, "word/commentsIds.xml");

    private static bool HasExtendedMetadata(string path) => Has(path, "word/commentsExtensible.xml");

    private static bool Has(string path, string part) =>
        path.Length > 0 && Parts(File.ReadAllBytes(path)).Contains(part);

    private static HashSet<string> Parts(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return [.. archive.Entries.Select(static entry => entry.FullName)];
    }

    /// <summary>
    /// Everything about the comments of a document that a save has to preserve: who said what,
    /// what answers what, and every mark and reference with the offset it sits at.
    /// </summary>
    /// <param name="document">The document to describe.</param>
    private static string Fingerprint(WordDocument document)
    {
        var text = new StringBuilder();
        foreach (Comment comment in document.Comments)
        {
            text.Append($"C[{comment.Id}|{comment.Author}|{comment.Initials}")
                .Append($"|parent={comment.ParentId}|done={comment.IsResolved}|")
                .Append(comment.GetText().Trim())
                .Append("] ");
        }

        int index = 0;
        foreach (BlockContainer container in document.AllContainers)
        {
            foreach (Paragraph paragraph in container.Blocks.Paragraphs)
            {
                index++;
                foreach ((int offset, InlineMark mark) in paragraph.Marks)
                {
                    if (mark is CommentRangeStart start)
                        text.Append($"start{start.Id}@{index}:{offset} ");
                    else if (mark is CommentRangeEnd end)
                        text.Append($"end{end.Id}@{index}:{offset} ");
                }

                foreach ((int offset, InlineObject anchored) in paragraph.Objects)
                {
                    if (anchored is CommentReference reference)
                        text.Append($"ref{reference.Id}@{index}:{offset} ");
                }
            }
        }

        return text.ToString();
    }
}
