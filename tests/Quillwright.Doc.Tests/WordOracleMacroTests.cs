using System.Globalization;
using System.Runtime.Versioning;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Puts a macro-enabled package Quillwright saved in front of Word, to confirm the VBA project
/// is still a project and not merely a run of bytes that happens to be there.
/// </summary>
/// <remarks>
/// Copying the project part through untouched is only half the claim. Word has to recognise the
/// result: the part has to keep its content type, its relationship and the macro-enabled type on
/// the main part, or a file that looks whole here opens without macros there. Only Word can say
/// so, and it says so through <c>HasVBProject</c>, which needs no trust setting to read.
/// </remarks>
[Trait("Category", "word-oracle")]
[SupportedOSPlatform("windows")]
public class WordOracleMacroTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "macros.docm");

    [Fact]
    public async Task AProjectCopiedThroughASave_IsStillAProjectToWord()
    {
        Assert.SkipUnless(WordOracle.Enabled, "Set QUILLWRIGHT_WORD_ORACLE=1 and install Word to run the oracle tests.");
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        WordDocument document = await WordDocument.LoadAsync(
            Fixture, cancellationToken: TestContext.Current.CancellationToken);

        string path = Path.Combine(Path.GetTempPath(), $"quillwright-oracle-{Guid.NewGuid():N}.docm");
        await using (FileStream file = File.Create(path))
        {
            await document.SaveAsync(file, cancellationToken: TestContext.Current.CancellationToken);
        }

        object hasProject = WordOracle.Inspect(path, static opened => WordOracle.Get(opened, "HasVBProject")!);

        Assert.True(Convert.ToBoolean(hasProject, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The same package read straight from Word's own output, as a control: if this failed the
    /// test above would be measuring the fixture rather than the save.
    /// </summary>
    [Fact]
    public void TheFixtureItself_HasAProjectToWord()
    {
        Assert.SkipUnless(WordOracle.Enabled, "Set QUILLWRIGHT_WORD_ORACLE=1 and install Word to run the oracle tests.");
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        string path = Path.Combine(Path.GetTempPath(), $"quillwright-oracle-{Guid.NewGuid():N}.docm");
        File.Copy(Fixture, path, overwrite: true);

        object hasProject = WordOracle.Inspect(path, static opened => WordOracle.Get(opened, "HasVBProject")!);

        Assert.True(Convert.ToBoolean(hasProject, CultureInfo.InvariantCulture));
    }
}
