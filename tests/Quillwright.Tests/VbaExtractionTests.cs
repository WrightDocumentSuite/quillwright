using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Vba;

namespace Quillwright.Tests;

/// <summary>
/// Covers reading a VBA project out of a macro-enabled package.
/// </summary>
/// <remarks>
/// The fixture is built by Word itself — see <c>tests/fixtures/build-fixtures.ps1</c> — so the
/// compressed source these tests decode is Microsoft's own output and not a round trip through
/// our own encoder. One module is deliberately long enough that its compressed form spans more
/// than one chunk, which is where the container's back-reference arithmetic resets.
/// </remarks>
public class VbaExtractionTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "macros.docm");

    private static async Task<VbaProject> LoadProjectAsync()
    {
        WordDocument document = await WordDocument.LoadAsync(Fixture, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(document.Macros);
        return document.Macros;
    }

    [Fact]
    public async Task AMacroEnabledDocument_YieldsItsModules()
    {
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        WordDocument document = await WordDocument.LoadAsync(Fixture, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(document.IsMacroEnabled);
        Assert.NotNull(document.Macros);
        VbaProject project = document.Macros;
        Assert.Equal("Project", project.Name);
        Assert.Equal(
            ["Bulk", "Greeting", "Helper", "ThisDocument"],
            project.Modules.Select(static m => m.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EachModule_IsClassifiedByWhatItBelongsTo()
    {
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        VbaProject project = await LoadProjectAsync();

        Assert.Equal(VbaModuleKind.Document, Kind(project, "ThisDocument"));
        Assert.Equal(VbaModuleKind.Procedural, Kind(project, "Greeting"));
        Assert.Equal(VbaModuleKind.Procedural, Kind(project, "Bulk"));
        Assert.Equal(VbaModuleKind.Class, Kind(project, "Helper"));
    }

    [Fact]
    public async Task TheSourceOfAModule_ComesBackAsItWasWritten()
    {
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        VbaProject project = await LoadProjectAsync();
        string code = Module(project, "Greeting").Code;

        Assert.Contains("Option Explicit", code, StringComparison.Ordinal);
        Assert.Contains("' A greeting for the reader.", code, StringComparison.Ordinal);
        Assert.Contains("Public Sub SayHello()", code, StringComparison.Ordinal);
        Assert.Contains("MsgBox \"Hello from Quillwright\"", code, StringComparison.Ordinal);

        // Identifiers are compared without regard to case: the editor unifies the spelling of a
        // name across the whole project, so "value" here follows the "Value" of the class module.
        Assert.Contains("Doubled = value * 2", code, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A compressed chunk expands to at most 4096 bytes, so anything longer is stored as
    /// several, each restarting the window that back-references are measured against.
    /// </summary>
    [Fact]
    public async Task AModuleTooLongForOneChunk_ComesBackWhole()
    {
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        VbaProject project = await LoadProjectAsync();
        string code = Module(project, "Bulk").Code;

        Assert.True(code.Length > 20000, $"Expected the long module to survive, got {code.Length} characters.");
        for (int i = 0; i < 220; i++)
        {
            Assert.Contains($"Public Function Step{i}(ByVal value As Long) As Long", code, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"    Step{i} = value + {i}", code, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(220, code.Split("End Function", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task AClassModule_KeepsItsProperties()
    {
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        string code = Module(await LoadProjectAsync(), "Helper").Code;

        Assert.Contains("Private stored As String", code, StringComparison.Ordinal);
        Assert.Contains("Public Property Let Value(ByVal text As String)", code, StringComparison.Ordinal);
        Assert.Contains("Public Property Get Value() As String", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Word gives every document a <c>ThisDocument</c> module whether or not it holds code, so
    /// the attribute preamble alone has to read as empty.
    /// </summary>
    [Fact]
    public async Task AModuleThatOnlyDeclaresItself_ReadsAsEmpty()
    {
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        VbaProject project = await LoadProjectAsync();

        Assert.True(Module(project, "ThisDocument").IsEmpty);
        Assert.False(Module(project, "Greeting").IsEmpty);
    }

    [Fact]
    public async Task TheProjectSurvivesARoundTrip_UnchangedAndStillReadable()
    {
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        WordDocument document = await WordDocument.LoadAsync(Fixture, cancellationToken: TestContext.Current.CancellationToken);
        var saved = new MemoryStream();
        await document.SaveAsync(saved, cancellationToken: TestContext.Current.CancellationToken);

        saved.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(saved, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(reloaded.IsMacroEnabled);
        Assert.NotNull(reloaded.Macros);
        Assert.Equal(document.Macros!.ToSourceListing(), reloaded.Macros.ToSourceListing());
    }

    [Fact]
    public async Task ADocumentWithoutMacros_HasNoProject()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("plain"));
        var saved = new MemoryStream();
        await document.SaveAsync(saved, cancellationToken: TestContext.Current.CancellationToken);

        saved.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(saved, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reloaded.Macros);
        Assert.False(reloaded.IsMacroEnabled);
    }

    [Fact]
    public async Task TheListing_NamesEveryModule()
    {
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        string listing = (await LoadProjectAsync()).ToSourceListing();

        Assert.Contains("=== Greeting (Procedural) ===", listing, StringComparison.Ordinal);
        Assert.Contains("=== Helper (Class) ===", listing, StringComparison.Ordinal);
        Assert.Contains("=== ThisDocument (Document) ===", listing, StringComparison.Ordinal);
    }

    /// <summary>
    /// A project cut short is read for what it still holds. Files of unknown provenance are
    /// exactly what this code is pointed at, so damage has to end in a partial answer.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(16)]
    public void ATruncatedProject_IsReadForWhatSurvives(int divisor)
    {
        Assert.SkipUnless(File.Exists(Fixture), "The macro fixture is not present.");

        byte[] whole = VbaFixtures.ProjectBytes("macros.docm");
        byte[] cut = whole[..(whole.Length / divisor)];

        Assert.Null(Record.Exception(() =>
        {
            if (CompoundFile.IsCompoundFile(cut))
                VbaProject.Read(CompoundFile.Open(cut), string.Empty);
        }));
    }

    private static VbaModule Module(VbaProject project, string name) =>
        Assert.Single(project.Modules, module => module.Name == name);

    private static VbaModuleKind Kind(VbaProject project, string name) => Module(project, name).Kind;
}
