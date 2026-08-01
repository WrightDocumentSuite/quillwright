namespace Quillwright.Testing;

/// <summary>
/// The two collections of real Word documents the corpus tests read, and where to find them.
/// </summary>
/// <remarks>
/// <para>
/// A great deal of what this library claims is checked against files Word itself produced over
/// two decades rather than against files these tests wrote. Those collections belong to other
/// projects and are far too large to vendor, so they are not part of this repository: a clone
/// has the code and not the corpus, and every test that needs one skips with <see cref="Absent"/>
/// rather than failing.
/// </para>
/// <para>
/// Three ways to point the tests at a corpus, tried in this order:
/// </para>
/// <list type="number">
///   <item><description>
///     <c>QUILLWRIGHT_CORPUS_OPENXML</c> and <c>QUILLWRIGHT_CORPUS_TELERIK</c>, each the full
///     path of one collection.
///   </description></item>
///   <item><description>
///     <c>QUILLWRIGHT_CORPUS</c>, a directory holding both under their own names.
///   </description></item>
///   <item><description>
///     Nothing at all, in which case the directory the repository sits in is searched for them.
///     Unpack them beside the repository and the tests find them.
///   </description></item>
/// </list>
/// <para>
/// A collection that is not there is the empty string rather than <see langword="null"/>:
/// <see cref="Directory.Exists(string)"/> and <see cref="File.Exists(string)"/> are both false
/// for it, so a test guards the way it would have guarded against a wrong path, and nothing
/// downstream has to carry a null.
/// </para>
/// </remarks>
public static class ReferenceCorpus
{
    /// <summary>
    /// Why a corpus test skipped, and what to unpack to make it run. A skip message that only
    /// says the data is missing leaves the reader no way forward, so this one names the source.
    /// </summary>
    public const string Absent =
        "No reference corpus was found. These checks read real documents from two collections " +
        "that are not part of this repository: the test assets of the Open XML SDK " +
        "(https://github.com/dotnet/Open-XML-SDK, under test/DocumentFormat.OpenXml.Tests.Assets) " +
        "and the test documents of Telerik Document Processing " +
        "(https://github.com/telerik/document-processing-sdk). Unpack them beside the repository " +
        "as 'Open-XML-SDK-<version>' and 'DocumentsTelerik', or set QUILLWRIGHT_CORPUS to a " +
        "directory holding both, or QUILLWRIGHT_CORPUS_OPENXML and QUILLWRIGHT_CORPUS_TELERIK to " +
        "each of them.";

    /// <summary>The Open XML SDK test assets, or the empty string when they are not here.</summary>
    public static string OpenXmlSdk { get; } = Locate("QUILLWRIGHT_CORPUS_OPENXML", "Open-XML-SDK-*");

    /// <summary>The Telerik test documents, or the empty string when they are not here.</summary>
    public static string Telerik { get; } = Locate("QUILLWRIGHT_CORPUS_TELERIK", "DocumentsTelerik");

    /// <summary>Whichever of the two collections is present, in the order the tests sweep them.</summary>
    public static string[] Roots { get; } =
        [.. new[] { OpenXmlSdk, Telerik }.Where(static root => root.Length > 0)];

    /// <summary>Whether any corpus at all is present.</summary>
    public static bool IsAvailable => Roots.Length > 0;

    /// <summary>A path inside the Open XML SDK assets, or the empty string when they are absent.</summary>
    public static string OpenXmlPath(string relative) => Combine(OpenXmlSdk, relative);

    /// <summary>A path inside the Telerik documents, or the empty string when they are absent.</summary>
    public static string TelerikPath(string relative) => Combine(Telerik, relative);

    /// <summary>
    /// A path inside the Open XML SDK assets that is there, skipping the calling test when it
    /// is not, so that the caller can use the path without a check of its own.
    /// </summary>
    public static string RequireOpenXmlPath(string relative) => Require(OpenXmlPath(relative));

    /// <summary>A path inside the Telerik documents that is there, or a skip.</summary>
    public static string RequireTelerikPath(string relative) => Require(TelerikPath(relative));

    /// <summary>Every file matching a pattern anywhere in either collection.</summary>
    public static IEnumerable<string> Files(string pattern) =>
        Roots.SelectMany(root => FilesUnder(root, pattern));

    /// <summary>
    /// Every file matching a pattern under one root, and nothing at all when that root is
    /// absent. <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> throws on
    /// the empty string rather than yielding nothing, which would turn a missing corpus into a
    /// failure at the very moment it is supposed to become a skip.
    /// </summary>
    public static IEnumerable<string> FilesUnder(string root, string pattern) =>
        root.Length == 0 || !Directory.Exists(root)
            ? []
            : Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);

    private static string Require(string path)
    {
        Assert.SkipWhen(!Path.Exists(path), Absent);
        return path;
    }

    // Deliberately not Path.Combine on an absent root: that would hand back the relative part
    // on its own, which resolves against whatever directory the test host happens to be in.
    private static string Combine(string root, string relative) =>
        root.Length == 0 ? string.Empty : Path.Combine(root, relative);

    private static string Locate(string variable, string pattern)
    {
        string? explicitPath = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Directory.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : string.Empty;

        string? container = Environment.GetEnvironmentVariable("QUILLWRIGHT_CORPUS");
        if (string.IsNullOrWhiteSpace(container) || !Directory.Exists(container))
            container = BesideTheRepository();

        if (container is null)
            return string.Empty;

        // A glob rather than a name, because the SDK assets carry their version in the
        // directory name and pinning one here would go stale the next time they are refreshed.
        return Directory.EnumerateDirectories(container, pattern)
            .Order(StringComparer.OrdinalIgnoreCase)
            .LastOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// The directory the repository sits in, found by walking up from the test binary until the
    /// solution file appears. Null when the tests run from somewhere that is not a checkout at
    /// all, which is the case that has to skip rather than throw.
    /// </summary>
    private static string? BesideTheRepository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Quillwright.slnx")))
                return directory.Parent?.FullName;
        }

        return null;
    }
}
