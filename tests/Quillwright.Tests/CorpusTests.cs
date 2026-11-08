using System.IO.Compression;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using Quillwright.Diagnostics;
using Quillwright.Formats;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Loads and re-saves every real document the reference repositories ship with.
/// </summary>
/// <remarks>
/// A corpus of files produced by Word over two decades finds the things a hand-written test
/// never thinks of: Strict-namespace packages, macro-enabled documents, VML fallbacks,
/// glossary parts, custom XML. Loadable documents must save and reload with the same text,
/// retain every original relationship, and keep every copy-through part byte-identical.
/// Inputs that the Open XML SDK validates cleanly must remain clean. Known encrypted inputs
/// are a separate, explicit refusal oracle rather than an exception-shaped success path.
/// </remarks>
public class CorpusTests
{
    private const string PasswordRequired =
        "The document is encrypted. Supply the password through LoadOptions to open it.";

    private static readonly string[] CorpusRoots = ReferenceCorpus.Roots;

    // These are the only files in the documented 934-file snapshot that are not expected to
    // load. Keeping the collection and relative path here means a new refusal fails the main
    // theory, while a fixed or renamed refusal fails the dedicated theory below.
    private static readonly ExpectedRefusal[] ExpectedRefusalManifest =
    [
        new(
            CorpusCollection.OpenXmlSdk,
            "test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/wordprocessing/protected/document with password.docx",
            PasswordRequired),
        new(
            CorpusCollection.OpenXmlSdk,
            "test/DocumentFormat.OpenXml.Tests.Assets/assets/TestDataStorage/v2FxTestFiles/wordprocessing/unable to normalized/document with password.docx",
            PasswordRequired),
    ];

    private static readonly HashSet<string> ExpectedRefusalPaths =
        ExpectedRefusalManifest.Select(static refusal => refusal.Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // The SDK validator itself throws for these deliberately malformed assets instead of
    // returning ValidationErrorInfo objects. They still exercise Quillwright's preservation
    // oracle, but they cannot support the clean-input-stays-clean validation claim.
    private static readonly ExpectedValidatorRejection[] ExpectedValidatorRejectionManifest =
    [
        new(
            CorpusCollection.OpenXmlSdk,
            "test/DocumentFormat.OpenXml.Tests.Assets/assets/TestFiles/InvalidDocProps.docx",
            nameof(InvalidOperationException),
            "Part: /docProps/core.xml doesn't exist in the package."),
        new(
            CorpusCollection.OpenXmlSdk,
            "test/DocumentFormat.OpenXml.Tests.Assets/assets/TestFiles/InvalidDocPropsct.docx",
            nameof(OpenXmlPackageException),
            "The document cannot be opened because there is an invalid part with an unexpected content type."),
    ];

    private static readonly HashSet<string> ExpectedValidatorRejectionPaths =
        ExpectedValidatorRejectionManifest
            .Select(static rejection => rejection.Identity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static TheoryData<string> Documents
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string path in EnumerateCorpus())
            {
                if (!ExpectedRefusalPaths.Contains(Identity(path)))
                    data.Add(path);
            }

            // A theory with no cases does not fail and does not skip: it vanishes, and the
            // total quietly drops. One empty case keeps the skip visible.
            if (data.Count == 0)
                data.Add(string.Empty);
            return data;
        }
    }

    public static TheoryData<string, string> ExpectedRefusals
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (ExpectedRefusal refusal in ExpectedRefusalManifest)
            {
                string root = Root(refusal.Collection);
                if (root.Length > 0)
                    data.Add(Path.Combine(root, refusal.RelativePath.Replace('/', Path.DirectorySeparatorChar)), refusal.Message);
            }

            if (data.Count == 0)
                data.Add(string.Empty, string.Empty);
            return data;
        }
    }

    public static TheoryData<string, string, string> ExpectedValidatorRejections
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (ExpectedValidatorRejection rejection in ExpectedValidatorRejectionManifest)
            {
                string root = Root(rejection.Collection);
                if (root.Length > 0)
                {
                    data.Add(
                        Path.Combine(root, rejection.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                        rejection.ExceptionType,
                        rejection.MessageFragment);
                }
            }

            if (data.Count == 0)
                data.Add(string.Empty, string.Empty, string.Empty);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task Document_LoadsAndSavesWithoutLosingParts(string path)
    {
        Assert.SkipWhen(path.Length == 0, ReferenceCorpus.Absent);

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        WordDocument document = await WordDocument.LoadAsync(path, cancellationToken: cancellationToken);

        Assert.NotEmpty(document.Sections);

        PreservedPackage originalPackage = Assert.IsType<PreservedPackage>(document.Preserved);
        var plan = new SavePlan(document);
        plan.Prepare();

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        AssertPartsSurvived(path, saved);
        AssertOpaquePartsUnchanged(path, originalPackage, plan.RegeneratedParts, saved);

        saved.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(saved, cancellationToken: cancellationToken);
        Assert.Equal(document.GetText(), reloaded.GetText());
        PreservedPackage reloadedPackage = Assert.IsType<PreservedPackage>(reloaded.Preserved);
        AssertRelationshipsSurvived(path, originalPackage, reloadedPackage);
        AssertOpaqueContentTypesSurvived(path, originalPackage, reloadedPackage, plan.RegeneratedParts);

        // Invalid corpus inputs are useful parser coverage but cannot prove the absence of a
        // new schema error. The precise guarantee is that every validator-clean input stays
        // validator-clean after the save.
        if (!ExpectedValidatorRejectionPaths.Contains(Identity(path)))
        {
            using Stream original = File.OpenRead(path);
            if (OpenXmlAssert.ValidatesCleanly(original))
                OpenXmlAssert.Valid(saved, Path.GetFileName(path));
        }
    }

    [Theory]
    [MemberData(nameof(ExpectedRefusals))]
    public async Task PasswordProtectedDocuments_AreExplicitExpectedRefusals(string path, string expectedMessage)
    {
        Assert.SkipWhen(path.Length == 0, ReferenceCorpus.Absent);
        Assert.True(File.Exists(path), $"The expected-refusal manifest is stale; '{path}' does not exist.");

        EncryptedDocumentException exception = await Assert.ThrowsAsync<EncryptedDocumentException>(async () =>
            await WordDocument.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(expectedMessage, exception.Message);
    }

    [Theory]
    [MemberData(nameof(ExpectedValidatorRejections))]
    public void StructurallyInvalidOriginals_AreExplicitValidatorRejections(
        string path,
        string expectedExceptionType,
        string expectedMessageFragment)
    {
        Assert.SkipWhen(path.Length == 0, ReferenceCorpus.Absent);
        Assert.True(File.Exists(path), $"The validator-rejection manifest is stale; '{path}' does not exist.");

        using Stream original = File.OpenRead(path);
        Exception exception = expectedExceptionType switch
        {
            nameof(InvalidOperationException) =>
                Assert.Throws<InvalidOperationException>(() => OpenXmlAssert.ValidatesCleanly(original)),
            nameof(OpenXmlPackageException) =>
                Assert.Throws<OpenXmlPackageException>(() => OpenXmlAssert.ValidatesCleanly(original)),
            _ => throw new InvalidOperationException($"Unknown expected validator exception '{expectedExceptionType}'."),
        };
        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertPartsSurvived(string originalPath, Stream saved)
    {
        HashSet<string> before = ReadPartNames(File.OpenRead(originalPath));
        saved.Position = 0;
        HashSet<string> after = ReadPartNames(saved, leaveOpen: true);

        // Relationship XML is regenerated, so byte identity is meaningless; its records are
        // checked semantically after reload. An empty .rels part may legitimately collapse.
        before.RemoveWhere(static name => name.Contains("_rels/", StringComparison.OrdinalIgnoreCase));
        after.RemoveWhere(static name => name.Contains("_rels/", StringComparison.OrdinalIgnoreCase));

        string[] missing = [.. before.Except(after).Order()];
        Assert.True(missing.Length == 0, $"{Path.GetFileName(originalPath)} lost: {string.Join(", ", missing)}");
    }

    private static HashSet<string> ReadPartNames(Stream stream, bool leaveOpen = false)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        return
        [
            .. archive.Entries
                .Where(static entry => !entry.FullName.EndsWith('/'))
                .Select(static entry => entry.FullName),
        ];
    }

    private static void AssertOpaquePartsUnchanged(
        string originalPath,
        PreservedPackage original,
        IReadOnlySet<string> regenerated,
        Stream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        Dictionary<string, ZipArchiveEntry> savedParts = archive.Entries
            .Where(static entry => !entry.FullName.EndsWith('/'))
            .ToDictionary(static entry => "/" + entry.FullName, StringComparer.OrdinalIgnoreCase);

        foreach ((string path, byte[] bytes) in original.Parts.OrderBy(static part => part.Key, StringComparer.Ordinal))
        {
            // Some producer ZIPs contain explicit directory entries. They are not OPC parts,
            // carry no content type or relationship semantics, and need not be reproduced.
            if (path.EndsWith('/') || regenerated.Contains(path))
                continue;

            Assert.True(
                savedParts.TryGetValue(path, out ZipArchiveEntry? entry),
                $"{Path.GetFileName(originalPath)} lost opaque part '{path}'.");
            using Stream content = entry.Open();
            byte[] expectedHash = SHA256.HashData(bytes);
            byte[] actualHash = SHA256.HashData(content);
            Assert.True(
                CryptographicOperations.FixedTimeEquals(expectedHash, actualHash),
                $"{Path.GetFileName(originalPath)} changed copy-through part '{path}': " +
                $"{Convert.ToHexString(expectedHash)} became {Convert.ToHexString(actualHash)}.");
        }
    }

    private static void AssertRelationshipsSurvived(
        string originalPath,
        PreservedPackage original,
        PreservedPackage reloaded)
    {
        foreach ((string source, List<OpcRelationship> relationships) in original.Relationships)
        {
            Assert.True(
                reloaded.Relationships.TryGetValue(source, out List<OpcRelationship>? savedRelationships),
                $"{Path.GetFileName(originalPath)} lost every relationship owned by '{source}'.");

            foreach (IGrouping<OpcRelationship, OpcRelationship> group in relationships.GroupBy(static relationship => relationship))
            {
                int actual = savedRelationships.Count(relationship => relationship == group.Key);
                Assert.True(
                    actual >= group.Count(),
                    $"{Path.GetFileName(originalPath)} lost or changed relationship from '{source}': " +
                    $"Id='{group.Key.Id}', Type='{group.Key.Type}', Target='{group.Key.Target}', " +
                    $"TargetMode='{(group.Key.IsExternal ? "External" : "Internal")}'.");
            }
        }
    }

    private static void AssertOpaqueContentTypesSurvived(
        string originalPath,
        PreservedPackage original,
        PreservedPackage reloaded,
        IReadOnlySet<string> regenerated)
    {
        foreach (string path in original.Parts.Keys.Where(path => !regenerated.Contains(path)))
        {
            if (path.EndsWith('/'))
                continue;

            string? expected = original.ContentTypes.GetContentType(path);
            string? actual = reloaded.ContentTypes.GetContentType(path);
            Assert.True(
                expected == actual,
                $"{Path.GetFileName(originalPath)} changed the content type of opaque part '{path}' " +
                $"from '{expected ?? "<none>"}' to '{actual ?? "<none>"}'.");
        }
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

    private static string Identity(string path)
    {
        foreach (CorpusCollection collection in Enum.GetValues<CorpusCollection>())
        {
            string root = Root(collection);
            if (root.Length == 0)
                continue;

            string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path)).Replace('\\', '/');
            if (!relative.StartsWith("../", StringComparison.Ordinal) && relative != "..")
                return $"{collection}:{relative}";
        }

        return path.Replace('\\', '/');
    }

    private static string Root(CorpusCollection collection) => collection switch
    {
        CorpusCollection.OpenXmlSdk => ReferenceCorpus.OpenXmlSdk,
        CorpusCollection.Telerik => ReferenceCorpus.Telerik,
        _ => string.Empty,
    };

    private enum CorpusCollection
    {
        OpenXmlSdk,
        Telerik,
    }

    private sealed record ExpectedRefusal(CorpusCollection Collection, string RelativePath, string Message)
    {
        public string Identity => $"{Collection}:{RelativePath}";
    }

    private sealed record ExpectedValidatorRejection(
        CorpusCollection Collection,
        string RelativePath,
        string ExceptionType,
        string MessageFragment)
    {
        public string Identity => $"{Collection}:{RelativePath}";
    }
}
