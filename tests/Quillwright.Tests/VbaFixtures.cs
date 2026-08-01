using System.IO.Compression;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Vba;

namespace Quillwright.Tests;

/// <summary>
/// Reaches the macro fixtures, which Word itself produced — see
/// <c>tests/fixtures/build-fixtures.ps1</c>.
/// </summary>
internal static class VbaFixtures
{
    /// <summary>Full path of a fixture.</summary>
    /// <param name="name">File name, such as <c>macros.docm</c>.</param>
    public static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>Loads a fixture and returns the project it carries.</summary>
    /// <param name="name">File name, such as <c>macros.docm</c>.</param>
    public static VbaProject Read(string name)
    {
        WordDocument document = WordDocument.LoadAsync(Path(name)).AsTask().GetAwaiter().GetResult();
        Assert.NotNull(document.Macros);
        return document.Macros;
    }

    /// <summary>
    /// Loads a legacy fixture and returns the project it carries. Read straight out of the
    /// <c>Macros</c> storage rather than through <c>Quillwright.Doc</c>, which this test project
    /// does not reference and does not need to for the project itself.
    /// </summary>
    /// <param name="name">File name, such as <c>macros.doc</c>.</param>
    public static VbaProject ReadLegacy(string name)
    {
        VbaProject? project = VbaProject.Read(CompoundFile.Open(File.ReadAllBytes(Path(name))), "Macros");
        Assert.NotNull(project);
        return project;
    }

    /// <summary>Opens the <c>vbaProject.bin</c> part of a fixture as a compound file.</summary>
    /// <param name="name">File name, such as <c>macros.docm</c>.</param>
    public static CompoundFile OpenProject(string name) => CompoundFile.Open(ProjectBytes(name));

    /// <summary>The raw <c>vbaProject.bin</c> part of a fixture.</summary>
    /// <param name="name">File name, such as <c>macros.docm</c>.</param>
    public static byte[] ProjectBytes(string name)
    {
        using ZipArchive archive = ZipFile.OpenRead(Path(name));
        using Stream stream = archive.GetEntry("word/vbaProject.bin")!.Open();
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
