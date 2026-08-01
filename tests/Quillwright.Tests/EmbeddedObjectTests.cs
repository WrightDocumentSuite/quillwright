using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// What a document carries besides its own text: spreadsheets, equations, and the plain files
/// someone attached.
/// </summary>
public class EmbeddedObjectTests
{
    private static readonly string[] CorpusRoots = ReferenceCorpus.Roots;

    [Fact]
    public async Task EmbeddedObjects_AreFoundWithTheirProgramAndTheirBytes()
    {
        List<EmbeddedObject> objects = await CorpusObjectsAsync();

        Assert.SkipWhen(objects.Count == 0, ReferenceCorpus.Absent);
        Assert.Contains(objects, static o => o.ProgramId is { Length: > 0 });
        Assert.All(objects, static o => Assert.NotEmpty(o.Content.ToArray()));
        Assert.All(objects, static o => Assert.StartsWith("/word/embeddings/", o.Location, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The display name comes out of the object's own <c>\1CompObj</c> stream, so finding one
    /// proves the compound file inside the package was actually opened.
    /// </summary>
    [Fact]
    public async Task AnObjectStoredAsACompoundFile_NamesItself()
    {
        List<EmbeddedObject> objects = await CorpusObjectsAsync();

        Assert.SkipWhen(objects.Count == 0, ReferenceCorpus.Absent);
        Assert.Contains(objects, static o => o.DisplayName is { Length: > 0 });
    }

    /// <summary>Word caches a picture of every embedded object, and that picture is a picture.</summary>
    [Fact]
    public async Task AnObjectWithACachedPicture_ResolvesIt()
    {
        List<EmbeddedObject> objects = await CorpusObjectsAsync();

        Assert.SkipWhen(objects.Count == 0, ReferenceCorpus.Absent);
        Assert.Contains(objects, static o => o.Preview is not null);
        Assert.All(
            objects.Where(static o => o.Preview is not null),
            static o => Assert.NotEmpty(o.Preview!.Bytes.ToArray()));
    }

    [Fact]
    public async Task EmbeddedObjects_SurviveTheRoundTripWithTheirBytesUnchanged()
    {
        string? path = FindWithEmbedding();
        Assert.SkipWhen(path is null, ReferenceCorpus.Absent);

        WordDocument document = await LoadAsync(path!);
        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        WordDocument reloaded = await WordDocument.LoadAsync(saved, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(document.EmbeddedObjects.Count, reloaded.EmbeddedObjects.Count);
        for (int i = 0; i < document.EmbeddedObjects.Count; i++)
        {
            Assert.Equal(
                document.EmbeddedObjects[i].Content.ToArray(),
                reloaded.EmbeddedObjects[i].Content.ToArray());
        }
    }

    private static async Task<List<EmbeddedObject>> CorpusObjectsAsync()
    {
        List<EmbeddedObject> objects = [];
        foreach (string path in Corpus().Take(400))
        {
            try
            {
                objects.AddRange((await LoadAsync(path)).EmbeddedObjects);
            }
            catch (Diagnostics.DocxFormatException)
            {
                // A corpus of test files includes deliberately corrupt ones.
            }
        }

        return objects;
    }

    private static string? FindWithEmbedding() =>
        Corpus().FirstOrDefault(static path =>
        {
            using var archive = new System.IO.Compression.ZipArchive(File.OpenRead(path));
            return archive.Entries.Any(static e => e.FullName.StartsWith("word/embeddings/", StringComparison.Ordinal));
        });

    private static ValueTask<WordDocument> LoadAsync(string path) =>
        WordDocument.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken);

    private static IEnumerable<string> Corpus()
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
