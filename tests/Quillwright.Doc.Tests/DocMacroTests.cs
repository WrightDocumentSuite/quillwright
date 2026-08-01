using Quillwright.Diagnostics;
using Quillwright.Doc.Writing;
using Quillwright.Model;
using Quillwright.Vba;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Covers reading a VBA project out of a legacy document, where it lives in a storage of the
/// document's own compound file rather than in a part of its own.
/// </summary>
/// <remarks>
/// Both fixtures come from one Word session saving the same document twice, so the two formats
/// must yield the same source — which is the point worth pinning, since the storage layout
/// around the project differs while the project itself does not.
/// </remarks>
public class DocMacroTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static VbaProject LoadProject()
    {
        WordDocument document = DocReader.Load(File.ReadAllBytes(Fixture("macros.doc")));
        Assert.NotNull(document.Macros);
        return document.Macros;
    }

    [Fact]
    public void ALegacyDocumentWithMacros_YieldsItsModules()
    {
        Assert.SkipUnless(File.Exists(Fixture("macros.doc")), "The macro fixture is not present.");

        VbaProject project = LoadProject();

        Assert.Equal("Project", project.Name);
        Assert.Equal(
            ["Bulk", "Greeting", "Helper", "ThisDocument"],
            project.Modules.Select(static m => m.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheSourceReadFromBothFormats_IsTheSame()
    {
        Assert.SkipUnless(File.Exists(Fixture("macros.doc")), "The macro fixture is not present.");
        Assert.SkipUnless(File.Exists(Fixture("macros.docm")), "The macro fixture is not present.");

        VbaProject legacy = LoadProject();
        WordDocument modern = await WordDocument.LoadAsync(
            Fixture("macros.docm"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(modern.Macros);
        Assert.Equal(modern.Macros.ToSourceListing(), legacy.ToSourceListing());
    }

    [Fact]
    public void ALongModule_ComesBackWholeFromTheLegacyFormat()
    {
        Assert.SkipUnless(File.Exists(Fixture("macros.doc")), "The macro fixture is not present.");

        string code = Assert.Single(LoadProject().Modules, static m => m.Name == "Bulk").Code;

        Assert.Contains("Public Function Step0(", code, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Step219 = value + 219", code, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(220, code.Split("End Function", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ALegacyDocumentWithoutMacros_HasNoProject()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("plain"));

        Assert.Null(DocReader.Load(DocWriter.Save(document)).Macros);
    }

    /// <summary>
    /// Macros are read, never written. Saving a document that carries them to <c>.doc</c> has
    /// to say so rather than quietly produce a file that no longer runs.
    /// </summary>
    [Fact]
    public void SavingADocumentWithMacros_WarnsThatTheyAreDropped()
    {
        Assert.SkipUnless(File.Exists(Fixture("macros.doc")), "The macro fixture is not present.");

        WordDocument document = DocReader.Load(File.ReadAllBytes(Fixture("macros.doc")));
        var warnings = new List<DocumentWarning>();

        byte[] saved = DocWriter.Save(document, new DocWriteOptions { OnWarning = warnings.Add });

        Assert.Contains(warnings, static w => w.Message.Contains("VBA project", StringComparison.Ordinal));
        Assert.Null(DocReader.Load(saved).Macros);
    }

    /// <summary>
    /// A user form makes Word write a control reference, whose extended half is the record most
    /// easily framed wrongly — and framing it wrongly loses every module declared after it.
    /// </summary>
    [Fact]
    public void ALegacyDocumentWithAForm_KeepsItsModulesAndReferences()
    {
        Assert.SkipUnless(File.Exists(Fixture("macros-forms.doc")), "The macro fixture is not present.");

        WordDocument document = DocReader.Load(File.ReadAllBytes(Fixture("macros-forms.doc")));
        Assert.NotNull(document.Macros);

        Assert.Equal(
            ["Launcher", "Scripted", "ThisDocument"],
            document.Macros.Modules.Select(static m => m.Name).Order(StringComparer.Ordinal));
        Assert.Contains(document.Macros.References, static r => r.Name == "MSForms" && r.Kind == VbaReferenceKind.Control);
        Assert.Contains(document.Macros.References, static r => r.Name == "Scripting");

        VbaModule launcher = Assert.Single(document.Macros.Modules, static m => m.Name == "Launcher");
        Assert.Equal(VbaModuleKind.Form, launcher.Kind);
        Assert.Contains("GoButton_Click", launcher.Code, StringComparison.Ordinal);
        Assert.NotNull(launcher.Designer);
    }

    /// <summary>
    /// A password stops the editor, not the reader. The legacy path has to say the same about a
    /// locked project as the modern one does — the lock is recorded in the <c>PROJECT</c> stream
    /// of the project itself, which both formats carry unchanged.
    /// </summary>
    [Fact]
    public void ALegacyDocumentWithALockedProject_ReadsItAndRecognisesThePassword()
    {
        Assert.SkipUnless(File.Exists(Fixture("macros-locked.doc")), "The locked macro fixture is not present.");

        WordDocument document = DocReader.Load(File.ReadAllBytes(Fixture("macros-locked.doc")));
        Assert.NotNull(document.Macros);

        Assert.Equal(
            ["Class1", "ThisDocument"],
            document.Macros.Modules.Select(static m => m.Name).Order(StringComparer.Ordinal));
        Assert.True(document.Macros.Protection.IsEditorProtected);
        Assert.False(document.Macros.Protection.IsVisible);
        Assert.True(document.Macros.Protection.IsPasswordCorrect("123"));
        Assert.False(document.Macros.Protection.IsPasswordCorrect("321"));
    }

    /// <summary>
    /// A document module Word created but nobody wrote in reads as empty, so a document that
    /// merely could run macros is told apart from one that does.
    /// </summary>
    [Fact]
    public void ModulesWithNothingInThem_ReadAsEmpty()
    {
        Assert.SkipUnless(File.Exists(Fixture("macros.doc")), "The macro fixture is not present.");

        VbaProject project = LoadProject();

        Assert.True(Assert.Single(project.Modules, static m => m.Name == "ThisDocument").IsEmpty);
        Assert.False(Assert.Single(project.Modules, static m => m.Name == "Helper").IsEmpty);
    }
}
