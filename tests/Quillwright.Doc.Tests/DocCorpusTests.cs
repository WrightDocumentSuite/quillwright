using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Reads every legacy document the reference repositories ship with and saves it as
/// <c>.docx</c>.
/// </summary>
/// <remarks>
/// The bar for a twenty-year-old binary format is not that every file converts perfectly —
/// it is that no file makes the reader hang, crash or produce a package Word would refuse.
/// A file the reader cannot handle has to say so with <see cref="DocFormatException"/>.
/// </remarks>
public class DocCorpusTests
{
    private static readonly string CorpusRoot = ReferenceCorpus.Telerik;

    public static TheoryData<string> Documents
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in Paths())
                data.Add(path);

            // A theory with no cases does not fail and does not skip: it vanishes, and the
            // total quietly drops. One empty case keeps the skip visible.
            if (data.Count == 0)
                data.Add(string.Empty);
            return data;
        }
    }

    private static IEnumerable<string> Paths()
    {
        if (!Directory.Exists(CorpusRoot))
            yield break;

        foreach (string path in Directory.EnumerateFiles(CorpusRoot, "*.doc", SearchOption.AllDirectories))
        {
            if (new FileInfo(path).Length is > 0 and < 8 * 1024 * 1024)
                yield return path;
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task LegacyDocument_ConvertsToAValidPackage(string path)
    {
        Assert.SkipWhen(path.Length == 0, ReferenceCorpus.Absent);

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        WordDocument document;
        try
        {
            document = await DocReader.LoadAsync(path, cancellationToken);
        }
        catch (Exception error) when (RefusedByDesign.Matches(error))
        {
            return;
        }

        Assert.NotEmpty(document.Sections);

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: cancellationToken);
        buffer.Position = 0;

        using WordprocessingDocument saved = WordprocessingDocument.Open(buffer, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        ValidationErrorInfo[] errors = [.. validator.Validate(saved, cancellationToken)];
        if (errors.Length == 0)
            return;

        string report = string.Join(
            Environment.NewLine,
            errors.Take(5).Select(e => $"{e.ErrorType} at {e.Path?.XPath}: {e.Description}"));
        Assert.Fail($"{Path.GetFileName(path)} converted to an invalid package.{Environment.NewLine}{report}");
    }

    /// <summary>
    /// The dates and the comment tree of <c>AtrdExtra</c> ([MS-DOC] 2.9.5) read from files Word
    /// itself wrote, rather than only from what this library writes.
    /// </summary>
    /// <remarks>
    /// The array sits at a pair of the header that Word 2002 appended, past the end of the one
    /// a Word 97 file has. Reading it out of a file that never had it would produce dates from
    /// whatever bytes follow the header, so the check is that every date is a real one.
    /// </remarks>
    [Fact]
    public async Task CommentDatesAndReplies_AreReadFromTheCorpus()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        List<Comment> comments = [];
        foreach (string path in Paths())
        {
            try
            {
                comments.AddRange((await DocReader.LoadAsync(path, cancellationToken)).Comments);
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                // Encrypted and pre-Word 97 files are refused by design.
            }
        }

        Assert.SkipWhen(comments.Count == 0, ReferenceCorpus.Absent);
        Assert.Contains(comments, static comment => comment.Date is not null);
        Assert.Contains(comments, static comment => comment.ParentId is not null);
        Assert.All(comments, static comment => Assert.True(
            comment.Date is null or { Year: >= 1990 and <= 2100 },
            $"comment {comment.Id} came back dated {comment.Date:u}"));
    }
}
