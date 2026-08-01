using System.Buffers.Binary;
using System.Text;

namespace Quillwright.Vba;

/// <summary>
/// Reads the <c>dir</c> stream of a VBA project ([MS-OVBA] 2.3.4.2), which lists the project's
/// references and modules and, for each module, where its source begins in its own stream.
/// </summary>
/// <remarks>
/// <para>
/// The stream is a flat run of records, each an identifier, a length and that many bytes. Only
/// the version record carries a field the length leaves out; everything else, including the
/// reference records that look irregular, is walked by its length alone.
/// </para>
/// <para>
/// Text is stored twice, once in the project's code page and once in UTF-16. The UTF-16 copy is
/// preferred where present, because the code page may not be available to the process.
/// </para>
/// </remarks>
internal sealed partial class VbaDirectory
{
    private VbaDirectory()
    {
    }

    /// <summary>Code page the project's single-byte text is written in.</summary>
    public int CodePage { get; private set; } = 1252;

    /// <summary>Name the project goes by in the editor.</summary>
    public byte[] ProjectName { get; private set; } = [];

    /// <summary>The modules the project declares, in the order the stream lists them.</summary>
    public List<VbaModuleRecord> Modules { get; } = [];

    /// <summary>The external libraries the project depends on.</summary>
    public List<VbaReference> References { get; } = [];

    /// <summary>Parses an already decompressed <c>dir</c> stream.</summary>
    /// <param name="dir">The decompressed bytes.</param>
    public static VbaDirectory Read(ReadOnlySpan<byte> dir)
    {
        var result = new VbaDirectory();
        VbaModuleRecord? module = null;
        int at = 0;

        while (at + 6 <= dir.Length)
        {
            int id = BinaryPrimitives.ReadUInt16LittleEndian(dir[at..]);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(dir[(at + 2)..]);
            at += 6;
            if (size < 0 || at + size > dir.Length)
                break;

            ReadOnlySpan<byte> data = dir.Slice(at, (int)size);
            if (!result.ApplyReference(id, data))
                module = result.ApplyModule(id, data, module) ?? result.ApplyProject(id, data);

            at += (int)size + Trailing(id);
        }

        result.ResolveReferences();
        return result;
    }

    /// <summary>Fields that follow a record without being counted in its length.</summary>
    /// <param name="id">Identifier of the record just read.</param>
    /// <remarks>
    /// Only the version record needs this. Every other record that looks irregular turns out to
    /// have a length covering all of it — the extended half of a control reference counts the
    /// type library and cookie that trail it, which the worked example in [MS-OVBA] 3.1.2 bears
    /// out: a <c>SizeExtended</c> of 0xA3 against 0x8F of fields ahead of them.
    /// </remarks>
    private static int Trailing(int id) => id == RecordId.Version ? 2 : 0;

    /// <summary>Takes in a record describing the project as a whole.</summary>
    /// <param name="id">Record identifier.</param>
    /// <param name="data">Record payload.</param>
    /// <returns>Always <see langword="null"/>: these records belong to no module.</returns>
    private VbaModuleRecord? ApplyProject(int id, ReadOnlySpan<byte> data)
    {
        switch (id)
        {
            case RecordId.CodePage when data.Length >= 2:
                CodePage = BinaryPrimitives.ReadUInt16LittleEndian(data);
                break;

            case RecordId.ProjectName:
                ProjectName = data.ToArray();
                break;
        }

        return null;
    }

    /// <summary>The encoding the project's single-byte text is written in.</summary>
    /// <remarks>
    /// Legacy code pages are not among the encodings a .NET process knows by default. Rather
    /// than register a provider for the whole process, which is not a library's decision to
    /// make, the code page provider is asked directly; Latin-1 stands in if even that has
    /// nothing, which keeps ASCII intact instead of throwing.
    /// </remarks>
    public Encoding TextEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CodePage);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(CodePage) ?? Encoding.Latin1;
        }
    }

    /// <summary>Identifiers of the records in a <c>dir</c> stream.</summary>
    private static class RecordId
    {
        public const int CodePage = 0x0003;
        public const int ProjectName = 0x0004;
        public const int Version = 0x0009;

        public const int ReferenceRegistered = 0x000D;
        public const int ReferenceProject = 0x000E;
        public const int ReferenceName = 0x0016;
        public const int ReferenceNameUnicode = 0x003E;
        public const int ReferenceControl = 0x002F;
        public const int ReferenceExtended = 0x0030;
        public const int ReferenceOriginal = 0x0033;

        public const int ModuleName = 0x0019;
        public const int ModuleNameUnicode = 0x0047;
        public const int ModuleStreamName = 0x001A;
        public const int ModuleStreamNameUnicode = 0x0032;
        public const int ModuleDescription = 0x001C;
        public const int ModuleDescriptionUnicode = 0x0048;
        public const int ModuleOffset = 0x0031;
        public const int ModuleDocument = 0x0022;
        public const int ModuleReadOnly = 0x0025;
        public const int ModulePrivate = 0x0028;
        public const int ModuleEnd = 0x002B;
    }
}
