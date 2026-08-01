using System.Buffers.Binary;
using System.Text;

namespace Quillwright.Vba;

internal sealed partial class VbaDirectory
{
    private readonly List<PendingReference> _references = [];
    private byte[] _pendingName = [];
    private string? _pendingUnicodeName;
    private byte[] _pendingOriginal = [];

    /// <summary>Takes in a record describing one module.</summary>
    /// <param name="id">Record identifier.</param>
    /// <param name="data">Record payload.</param>
    /// <param name="current">Module the preceding records belonged to.</param>
    /// <returns>The module now being described, or <see langword="null"/> when this was not one.</returns>
    private VbaModuleRecord? ApplyModule(int id, ReadOnlySpan<byte> data, VbaModuleRecord? current)
    {
        if (id == RecordId.ModuleName)
        {
            var started = new VbaModuleRecord { Name = data.ToArray() };
            Modules.Add(started);
            return started;
        }

        if (current is null)
            return null;

        switch (id)
        {
            case RecordId.ModuleNameUnicode: current.UnicodeName = Utf16(data); break;
            case RecordId.ModuleStreamName: current.StreamName = data.ToArray(); break;
            case RecordId.ModuleStreamNameUnicode: current.UnicodeStreamName = Utf16(data); break;
            case RecordId.ModuleDescription: current.Description = data.ToArray(); break;
            case RecordId.ModuleDescriptionUnicode:
                current.UnicodeDescription = Utf16(data) is { Length: > 0 } description ? description : null;
                break;

            case RecordId.ModuleDocument: current.IsDocumentModule = true; break;
            case RecordId.ModuleReadOnly: current.IsReadOnly = true; break;
            case RecordId.ModulePrivate: current.IsPrivate = true; break;
            case RecordId.ModuleEnd: return null;
            case RecordId.ModuleOffset when data.Length >= 4:
                current.TextOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
                break;
        }

        return current;
    }

    /// <summary>
    /// Takes in a record describing an external reference. A reference is spelled as a name
    /// followed by the thing named, so the name is held back until the kind of thing is known.
    /// </summary>
    /// <param name="id">Record identifier.</param>
    /// <param name="data">Record payload.</param>
    /// <returns>Whether the record belonged to a reference.</returns>
    private bool ApplyReference(int id, ReadOnlySpan<byte> data)
    {
        switch (id)
        {
            case RecordId.ReferenceName:
                _pendingName = data.ToArray();
                return true;

            case RecordId.ReferenceNameUnicode:
                _pendingUnicodeName = Utf16(data);
                return true;

            case RecordId.ReferenceRegistered:
                Begin(VbaReferenceKind.Registered, Counted(data));
                return true;

            case RecordId.ReferenceProject:
                Begin(VbaReferenceKind.Project, Counted(data));
                return true;

            case RecordId.ReferenceOriginal:
                // Wraps the control record that follows, so it is held for that record to take.
                _pendingOriginal = data.ToArray();
                return true;

            case RecordId.ReferenceControl:
                // The identifier here is a placeholder; the real one is in the extended half.
                Begin(VbaReferenceKind.Control, []);
                return true;

            case RecordId.ReferenceExtended when _references.Count > 0:
                if (Counted(data) is { Length: > 0 } extended)
                    _references[^1] = _references[^1] with { Libid = extended };

                // The name just read belonged to the extended library, not to whatever comes next.
                _pendingName = [];
                _pendingUnicodeName = null;
                return true;

            default:
                return false;
        }
    }

    /// <summary>Starts a reference, taking whatever name was read ahead of it.</summary>
    /// <param name="kind">What kind of thing is referenced.</param>
    /// <param name="libid">Raw bytes of the identifier.</param>
    private void Begin(VbaReferenceKind kind, byte[] libid)
    {
        _references.Add(new PendingReference(kind, _pendingName, _pendingUnicodeName, libid, _pendingOriginal));
        _pendingName = [];
        _pendingUnicodeName = null;
        _pendingOriginal = [];
    }

    /// <summary>
    /// The identifier out of a reference record, which begins with a length of its own ahead of
    /// the reserved fields that the record's own length also covers.
    /// </summary>
    /// <param name="data">Record payload.</param>
    private static byte[] Counted(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return [];

        long length = BinaryPrimitives.ReadUInt32LittleEndian(data);
        return length <= 0 || length > data.Length - 4 ? [] : data.Slice(4, (int)length).ToArray();
    }

    /// <summary>
    /// Turns the references into their final form, now that the code page is known. Nothing is
    /// decoded during the walk because the code page is only announced part way through it.
    /// </summary>
    private void ResolveReferences()
    {
        Encoding encoding = TextEncoding();
        foreach (PendingReference pending in _references)
        {
            References.Add(new VbaReference(
                pending.UnicodeName ?? encoding.GetString(pending.Name),
                encoding.GetString(pending.Libid),
                pending.Kind)
            {
                OriginalLibid = pending.Original.Length > 0 ? encoding.GetString(pending.Original) : null,
            });
        }
    }

    private static string Utf16(ReadOnlySpan<byte> data) => Encoding.Unicode.GetString(data).TrimEnd('\0');

    /// <summary>A reference read but not yet decoded.</summary>
    /// <param name="Kind">What kind of thing is referenced.</param>
    /// <param name="Name">The name, in the project's code page.</param>
    /// <param name="UnicodeName">The name in UTF-16, when the stream carried one.</param>
    /// <param name="Libid">The identifier, in the project's code page.</param>
    /// <param name="Original">The identifier the referenced library was generated from, if given.</param>
    private readonly record struct PendingReference(
        VbaReferenceKind Kind, byte[] Name, string? UnicodeName, byte[] Libid, byte[] Original);
}
