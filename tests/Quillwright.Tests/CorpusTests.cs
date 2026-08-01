using System.IO.Compression;
using Quillwright.Diagnostics;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Loads and re-saves every real document the reference repositories ship with.
/// </summary>
/// <remarks>
/// A corpus of files produced by Word over two decades finds the things a hand-written test
/// never thinks of: Strict-namespace packages, macro-enabled documents, VML fallbacks,
/// glossary parts, custom XML. The bar is that nothing throws, the saved package still
/// opens, and no part of the original is dropped on the way through.
/// </remarks>
public class CorpusTests
{
    private static readonly string[] CorpusRoots = ReferenceCorpus.Roots;

    public static TheoryData<string> Documents
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in EnumerateCorpus())
                data.Add(path);

            // A theory with no cases does not fail and does not skip: it vanishes, and the
            // total quietly drops. One empty case keeps the skip visible.
            if (data.Count == 0)
                data.Add(string.Empty);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task Document_LoadsAndSavesWithoutLosingParts(string path)
    {
        Assert.SkipWhen(path.Length == 0, ReferenceCorpus.Absent);

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        WordDocument document;
        try
        {
            document = await WordDocument.LoadAsync(path, cancellationToken: cancellationToken);
        }
        catch (DocxFormatException)
        {
            // A corpus of test files includes deliberately corrupt ones; refusing to open
            // them with a clear exception is the correct behaviour.
            return;
        }

        Assert.NotEmpty(document.Sections);

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        AssertPartsSurvived(path, saved);

        saved.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(saved, cancellationToken: cancellationToken);
        Assert.Equal(document.GetText(), reloaded.GetText());

        // Many files in a test corpus are deliberately malformed, so the bar is that saving
        // does not make a document worse than it arrived.
        if (ValidatesCleanly(File.OpenRead(path)))
            OpenXmlAssert.Valid(saved, Path.GetFileName(path));
    }

    private static bool ValidatesCleanly(Stream package)
    {
        using (package)
        {
            try
            {
                OpenXmlAssert.Valid(package, "original");
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private static void AssertPartsSurvived(string originalPath, Stream saved)
    {
        HashSet<string> before = ReadPartNames(File.OpenRead(originalPath));
        saved.Position = 0;
        HashSet<string> after = ReadPartNames(saved, leaveOpen: true);

        // Relationship parts are regenerated and may legitimately collapse when empty.
        before.RemoveWhere(static name => name.Contains("_rels/", StringComparison.OrdinalIgnoreCase));
        after.RemoveWhere(static name => name.Contains("_rels/", StringComparison.OrdinalIgnoreCase));

        string[] missing = [.. before.Except(after).Order()];
        Assert.True(missing.Length == 0, $"{Path.GetFileName(originalPath)} lost: {string.Join(", ", missing)}");
    }

    private static HashSet<string> ReadPartNames(Stream stream, bool leaveOpen = false)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        return [.. archive.Entries.Select(static entry => entry.FullName)];
    }

    private static IEnumerable<string> EnumerateCorpus()
    {
        foreach (string root in CorpusRoots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (string path in Directory.EnumerateFiles(root, "*.docx", SearchOption.AllDirectories))
            {
                if (new FileInfo(path).Length is > 0 and < 8 * 1024 * 1024)
                    yield return path;
            }
        }
    }
}
