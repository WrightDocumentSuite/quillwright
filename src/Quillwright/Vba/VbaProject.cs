using System.Text;
using Quillwright.IO;

namespace Quillwright.Vba;

/// <summary>
/// The VBA project carried by a macro-enabled document, with the source of every module.
/// </summary>
/// <remarks>
/// <para>
/// Word stores macros the same way in both of its formats: a small compound file holding a
/// <c>VBA</c> storage, which in <c>.docm</c> is the part <c>word/vbaProject.bin</c> and in
/// <c>.doc</c> is a storage named <c>Macros</c> inside the document itself. One reader
/// therefore serves both.
/// </para>
/// <para>
/// Reading is one way. The project is not modelled, only decoded, and saving a document copies
/// the original bytes through untouched — so the source seen here and the macros a saved file
/// runs are always the same, and no edit made here could change them.
/// </para>
/// <para>
/// A password on a project does not hide its source. The password guards the editor, while the
/// source sits beside it compressed but unencrypted, so a locked project reads like any other.
/// </para>
/// </remarks>
public sealed class VbaProject
{
    private VbaProject(string name, int codePage, IReadOnlyList<VbaModule> modules, IReadOnlyList<VbaReference> references, VbaProtection protection)
    {
        Name = name;
        CodePage = codePage;
        Modules = modules;
        References = references;
        Protection = protection;
    }

    /// <summary>Name the project goes by in the editor, usually <c>Project</c>.</summary>
    public string Name { get; }

    /// <summary>Code page the project's text was written in.</summary>
    public int CodePage { get; }

    /// <summary>Every module of the project, in the order it declares them.</summary>
    public IReadOnlyList<VbaModule> Modules { get; }

    /// <summary>The external libraries the project depends on, in the order it declares them.</summary>
    public IReadOnlyList<VbaReference> References { get; }

    /// <summary>Whether the project was locked, and by whom.</summary>
    public VbaProtection Protection { get; }

    /// <summary>The source of every module, in one listing with a header per module.</summary>
    public string ToSourceListing()
    {
        var builder = new StringBuilder();
        foreach (VbaModule module in Modules)
        {
            builder.Append("' === ").Append(module.Name).Append(" (").Append(module.Kind).AppendLine(") ===");
            builder.AppendLine(module.Code);
        }

        return builder.ToString();
    }

    /// <summary>Reads a project out of a compound file.</summary>
    /// <param name="container">The compound file the project lives in.</param>
    /// <param name="prefix">Path of the storage holding it, empty when it is the whole file.</param>
    /// <returns>The project, or <see langword="null"/> when the container holds none.</returns>
    internal static VbaProject? Read(CompoundFile container, string prefix)
    {
        string root = prefix.Length == 0 ? string.Empty : prefix + "/";
        if (container.ReadStream(root + "VBA/dir") is not { Length: > 0 } packed)
            return null;

        VbaDirectory directory = VbaDirectory.Read(VbaCompression.Decompress(packed));
        Encoding encoding = directory.TextEncoding();
        VbaProjectStream project = VbaProjectStream.Read(container.ReadStream(root + "PROJECT"), encoding);

        var modules = new List<VbaModule>(directory.Modules.Count);
        foreach (VbaModuleRecord record in directory.Modules)
            modules.Add(ReadModule(container, root, record, project, encoding));

        return new VbaProject(
            ProjectName(directory, project, encoding),
            directory.CodePage,
            modules,
            directory.References,
            VbaProtection.Read(project.ProtectionState, project.Password, project.Visibility, encoding));
    }

    /// <summary>Builds one module from what the directory said and what its stream holds.</summary>
    /// <param name="container">The compound file the project lives in.</param>
    /// <param name="root">Path prefix of the project inside the container.</param>
    /// <param name="record">What the directory stream said about the module.</param>
    /// <param name="project">What the <c>PROJECT</c> stream said about the project.</param>
    /// <param name="encoding">Encoding of the project's single-byte text.</param>
    private static VbaModule ReadModule(
        CompoundFile container, string root, VbaModuleRecord record, VbaProjectStream project, Encoding encoding)
    {
        string name = record.UnicodeName ?? encoding.GetString(record.Name);
        string streamName = record.UnicodeStreamName ?? encoding.GetString(record.StreamName);
        if (streamName.Length == 0)
            streamName = name;

        VbaModuleKind kind = Classify(name, record, project);
        byte[]? stream = container.ReadStream(root + "VBA/" + streamName);
        return new VbaModule(name, streamName, kind, stream is null ? string.Empty : ReadSource(stream, record.TextOffset, encoding))
        {
            Description = Describe(record, encoding),
            IsReadOnly = record.IsReadOnly,
            IsPrivate = record.IsPrivate,
            Designer = kind == VbaModuleKind.Form ? VbaDesigner.Read(container, root, streamName, encoding) : null,
        };
    }

    /// <summary>
    /// The description a module carries. The stream holds it twice, and the UTF-16 copy is
    /// preferred, but a writer that leaves that copy empty still means what the other one says.
    /// </summary>
    /// <param name="record">What the directory stream said about the module.</param>
    /// <param name="encoding">Encoding of the project's single-byte text.</param>
    private static string? Describe(VbaModuleRecord record, Encoding encoding) =>
        record.UnicodeDescription ?? (record.Description.Length > 0 ? encoding.GetString(record.Description) : null);

    private static string ProjectName(VbaDirectory directory, VbaProjectStream project, Encoding encoding) =>
        directory.ProjectName.Length > 0 ? encoding.GetString(directory.ProjectName) : project.Name ?? "Project";

    /// <summary>What kind of module this is, preferring what the <c>PROJECT</c> stream says.</summary>
    /// <param name="name">Module name.</param>
    /// <param name="record">What the directory stream said about it.</param>
    /// <param name="project">What the <c>PROJECT</c> stream said about the project.</param>
    private static VbaModuleKind Classify(string name, VbaModuleRecord record, VbaProjectStream project)
    {
        if (project.Kinds.TryGetValue(name, out VbaModuleKind kind))
            return kind;

        return record.IsDocumentModule ? VbaModuleKind.Document : VbaModuleKind.Procedural;
    }

    /// <summary>
    /// Pulls the source out of a module stream, which begins with an opaque cache of compiled
    /// state and only then holds the compressed text.
    /// </summary>
    /// <param name="stream">The whole module stream.</param>
    /// <param name="textOffset">Where the directory said the text begins.</param>
    /// <param name="encoding">Encoding of the decompressed text.</param>
    private static string ReadSource(byte[] stream, int textOffset, Encoding encoding)
    {
        if (textOffset >= 0 && textOffset < stream.Length && VbaCompression.LooksLikeContainer(stream.AsSpan(textOffset)))
            return Text(VbaCompression.Decompress(stream.AsSpan(textOffset)), encoding);

        // Without a usable offset the container still announces itself, so look for it.
        for (int at = 0; at < stream.Length; at++)
        {
            if (!VbaCompression.LooksLikeContainer(stream.AsSpan(at)))
                continue;

            byte[] decoded = VbaCompression.Decompress(stream.AsSpan(at));
            if (decoded.Length > 0)
                return Text(decoded, encoding);
        }

        return string.Empty;
    }

    /// <summary>
    /// The text of a decompressed container. A chunk the compressor could not shrink is stored
    /// whole and padded out to 4096 bytes with zeroes ([MS-OVBA] 2.4.1.3.10), which the format
    /// cannot tell apart from content, so a container can yield more than went into it.
    /// </summary>
    /// <param name="decoded">The decompressed bytes.</param>
    /// <param name="encoding">Encoding of the project's single-byte text.</param>
    private static string Text(byte[] decoded, Encoding encoding) => encoding.GetString(decoded).TrimEnd('\0');
}
