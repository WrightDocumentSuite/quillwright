using Quillwright.IO;
using Quillwright.Vba;

namespace Quillwright.Tests;

/// <summary>
/// Covers the external references a project declares, and the design-time properties of a form.
/// </summary>
/// <remarks>
/// The fixture behind these tests carries a user form, which is what makes it worth having: a
/// form obliges Word to write a control reference, and that record is the one whose framing the
/// specification describes least plainly. Reading it wrongly does not corrupt one reference — it
/// loses the whole module list that follows, so these tests guard the modules as much as the
/// references.
/// </remarks>
public class VbaReferenceTests
{
    private const string Fixture = "macros-forms.docm";

    [Fact]
    public void AProjectWithAForm_StillListsEveryModule()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        VbaProject project = VbaFixtures.Read(Fixture);

        Assert.Equal(
            ["Launcher", "Scripted", "ThisDocument"],
            project.Modules.Select(static m => m.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheReferencesTheProjectDeclares_AreReadWithTheirKinds()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        VbaProject project = VbaFixtures.Read(Fixture);

        Assert.Equal(VbaReferenceKind.Registered, Reference(project, "stdole").Kind);
        Assert.Equal(VbaReferenceKind.Registered, Reference(project, "Scripting").Kind);
        Assert.Equal(VbaReferenceKind.Project, Reference(project, "Normal").Kind);
        Assert.Equal(VbaReferenceKind.Control, Reference(project, "MSForms").Kind);
    }

    /// <summary>
    /// A control reference keeps its real identifier in its extended half, past a placeholder
    /// of all zeroes; picking up the placeholder instead would leave the reference nameless.
    /// </summary>
    [Fact]
    public void AControlReference_CarriesTheExtendedIdentifier()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        VbaReference forms = Reference(VbaFixtures.Read(Fixture), "MSForms");

        Assert.Contains("MSForms", forms.Libid, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{00000000-0000-0000-0000-000000000000}", forms.Libid, StringComparison.Ordinal);
        Assert.Equal("Microsoft Forms 2.0 Object Library", forms.Description);
    }

    /// <summary>
    /// The identifier in a control's own record names a cache file that Word generated under
    /// the temporary directory of the machine that saved the document, with a class identifier
    /// minted for that machine. The library it was generated from is named separately, and it
    /// is the one worth reporting — the other says nothing about what the document reaches.
    /// </summary>
    [Fact]
    public void AControlReference_AlsoNamesTheLibraryItWasGeneratedFrom()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        VbaProject project = VbaFixtures.Read(Fixture);
        VbaReference forms = Reference(project, "MSForms");

        Assert.NotNull(forms.OriginalLibid);
        Assert.Contains("{0D452EE1-E08F-101A-852E-02608C4D0BB4}", forms.OriginalLibid, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FM20.DLL", forms.OriginalLibid, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".exd", forms.Libid, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            project.References.Where(static r => r.Kind != VbaReferenceKind.Control),
            static reference => Assert.Null(reference.OriginalLibid));
    }

    [Fact]
    public void ARegisteredReference_CarriesItsLibraryDescription()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        VbaProject project = VbaFixtures.Read(Fixture);

        Assert.Equal("OLE Automation", Reference(project, "stdole").Description);
        Assert.Equal("Microsoft Scripting Runtime", Reference(project, "Scripting").Description);
        Assert.Contains("{420B2830-E718-11CF-893D-00A0C9054228}", Reference(project, "Scripting").Libid, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFormModule_IsClassifiedAsOneAndKeepsItsCode()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        VbaModule launcher = Module(VbaFixtures.Read(Fixture), "Launcher");

        Assert.Equal(VbaModuleKind.Form, launcher.Kind);
        Assert.Contains("Private Sub UserForm_Initialize()", launcher.Code, StringComparison.Ordinal);
        Assert.Contains("Private Sub GoButton_Click()", launcher.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void AFormModule_CarriesItsDesignTimeSize()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        VbaDesigner designer = Assert.IsType<VbaDesigner>(Module(VbaFixtures.Read(Fixture), "Launcher").Designer);

        Assert.Equal("UserForm1", designer.Caption);
        Assert.Equal("{C62A69F0-16DC-11CE-9E98-00AA00574A4F}", designer.ClassId, StringComparer.OrdinalIgnoreCase);
        Assert.InRange(designer.Width.Points, 100, 400);
        Assert.InRange(designer.Height.Points, 100, 400);
    }

    [Fact]
    public void AModuleThatIsNotAForm_HasNoDesigner()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        VbaProject project = VbaFixtures.Read(Fixture);

        Assert.Null(Module(project, "Scripted").Designer);
        Assert.Null(Module(project, "ThisDocument").Designer);
    }

    /// <summary>Word marks the modules that back a class or a form as private to the project.</summary>
    [Fact]
    public void ModulesWordScopesToTheProject_AreReportedAsPrivate()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        VbaProject project = VbaFixtures.Read(Fixture);

        Assert.True(Module(project, "Launcher").IsPrivate);
        Assert.False(Module(project, "Scripted").IsPrivate);
        Assert.False(Module(project, "Scripted").IsReadOnly);
    }

    [Fact]
    public void ModulesWithNoDescription_ReportNoneRatherThanAnEmptyOne()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        Assert.All(VbaFixtures.Read(Fixture).Modules, static module => Assert.Null(module.Description));
    }

    [Fact]
    public void TheLegacyCopy_ReadsTheSameReferences()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path("macros-forms.doc")), "The macro fixture is not present.");
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(Fixture)), "The macro fixture is not present.");

        VbaProject modern = VbaFixtures.Read(Fixture);
        CompoundFile legacy = CompoundFile.Open(File.ReadAllBytes(VbaFixtures.Path("macros-forms.doc")));
        VbaProject? binary = VbaProject.Read(legacy, "Macros");

        Assert.NotNull(binary);
        Assert.Equal(
            modern.References.Select(static r => $"{r.Name}/{r.Kind}"),
            binary.References.Select(static r => $"{r.Name}/{r.Kind}"));
    }

    private static VbaReference Reference(VbaProject project, string name) =>
        Assert.Single(project.References, reference => reference.Name == name);

    private static VbaModule Module(VbaProject project, string name) =>
        Assert.Single(project.Modules, module => module.Name == name);
}
