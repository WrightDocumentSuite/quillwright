using System.Buffers.Binary;
using System.Text;
using Quillwright.Diagnostics;

namespace Quillwright.IO;

/// <summary>
/// Reads a Compound File Binary container ([MS-CFB]) — the little file system a legacy
/// <c>.doc</c> is stored in, and the one a VBA project is stored in wherever it appears.
/// </summary>
/// <remarks>
/// <para>
/// The container holds named streams in a directory tree, with the bytes of each stream
/// scattered across fixed-size sectors and chained through a file allocation table. Small
/// streams live in a second, finer allocation inside a stream of their own. Only reading is
/// implemented, and the whole file is held in memory: a legacy Word document is measured in
/// megabytes, and random access across two allocation tables is what the format demands.
/// </para>
/// <para>
/// The directory is a tree, not a list — a macro project sits under <c>Macros/VBA</c>, and
/// two storages may each hold a stream called <c>dir</c>. Entries are therefore addressed by
/// path. Anything the tree does not reach is still registered under its bare name, because a
/// twenty-year-old file with a damaged tree is better read than refused.
/// </para>
/// </remarks>
internal sealed class CompoundFile
{
    private const int DirectoryEntrySize = 128;
    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;

    /// <summary>The largest number that names a real sector ([MS-CFB] 2.1); above it are markers.</summary>
    private const uint MaxRegularSector = 0xFFFFFFFA;

    private const int NoEntry = -1;

    private readonly byte[] _data;
    private readonly int _sectorSize;
    private readonly int _miniSectorSize;
    private readonly ulong _miniCutoff;
    private readonly bool _isVersion3;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly List<DirectoryEntry> _directory = [];
    private readonly Dictionary<string, DirectoryEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[] _miniStream;
    private readonly DocumentLoadBudget _budget;

    private CompoundFile(byte[] data, DocumentLoadBudget budget)
    {
        _data = data;
        _budget = budget;
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(Span(26, 2));
        ushort byteOrder = BinaryPrimitives.ReadUInt16LittleEndian(Span(28, 2));
        ushort sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(Span(30, 2));
        ushort miniSectorShift = BinaryPrimitives.ReadUInt16LittleEndian(Span(32, 2));
        if (version is not (3 or 4) || byteOrder != 0xFFFE ||
            (version == 3 && sectorShift != 9) || (version == 4 && sectorShift != 12) ||
            miniSectorShift != 6)
        {
            throw new CompoundFileException("The compound file header has an unsupported version or sector layout.");
        }

        _isVersion3 = version == 3;
        _sectorSize = 1 << sectorShift;
        _miniSectorSize = 1 << miniSectorShift;
        if (_data.Length < _sectorSize)
            throw new CompoundFileException("The compound file is shorter than its header sector.");

        // The cutoff is required to be 4096, but it is a field of the header rather than a
        // constant of the format, so it is read rather than assumed.
        uint cutoff = BinaryPrimitives.ReadUInt32LittleEndian(Span(56, 4));
        _miniCutoff = cutoff == 0 ? 4096 : cutoff;

        _fat = ReadFat();
        _miniFat = ReadChainValues(BinaryPrimitives.ReadUInt32LittleEndian(Span(60, 4)));
        ReadDirectory(BinaryPrimitives.ReadUInt32LittleEndian(Span(48, 4)));
        ValidateDirectory();

        // The mini stream is an ordinary stream owned by the root entry, allocated from the
        // main table whatever its size.
        _miniStream = _directory.Count > 0 && _directory[0].Size > 0
            ? ReadChain(_directory[0].StartSector, (int)_directory[0].Size, _sectorSize, _fat)
            : [];

        BuildPaths();
    }

    /// <summary>The paths of every stream in the container, storages included in the path.</summary>
    public IEnumerable<string> StreamNames => _byPath.Where(static e => e.Value.IsStream).Select(static e => e.Key);

    /// <summary>Opens a container from bytes.</summary>
    /// <param name="data">The whole file.</param>
    /// <param name="budget">Optional limits for the container directory and streams.</param>
    /// <exception cref="CompoundFileException">The bytes are not a compound file.</exception>
    public static CompoundFile Open(byte[] data, DocumentLoadBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        DocumentLoadBudget limits = budget ?? DocumentLoadBudget.Default;
        limits.Validate();
        DocumentLoadBudgetState.Ensure(
            nameof(DocumentLoadBudget.MaxInputBytes), limits.MaxInputBytes, data.LongLength);
        if (!IsCompoundFile(data))
            throw new CompoundFileException("The bytes do not begin with a compound file signature.");

        return new CompoundFile(data, limits);
    }

    /// <summary>Whether bytes look like a compound file.</summary>
    /// <param name="data">The bytes to test.</param>
    public static bool IsCompoundFile(ReadOnlySpan<byte> data) => data.Length >= 512 && HasSignature(data);

    /// <summary>Whether bytes open with the compound file signature ([MS-CFB] 2.2), header and all.</summary>
    /// <param name="data">The first bytes of a file, of which eight are enough.</param>
    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= Signature.Length && data[..Signature.Length].SequenceEqual(Signature);

    private static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>Returns the contents of a stream, or <see langword="null"/> when there is none.</summary>
    /// <param name="path">Full path such as <c>Macros/VBA/dir</c>, or a bare name for a stream of the root.</param>
    public byte[]? ReadStream(string path)
    {
        if (!_byPath.TryGetValue(path, out DirectoryEntry? entry) || !entry.IsStream)
            return null;

        // A stream shorter than the cutoff lives in the mini allocation ([MS-CFB] 2.6.3).
        return entry.Size < _miniCutoff && _miniStream.Length > 0
            ? ReadChain(entry.StartSector, (int)entry.Size, _miniSectorSize, _miniFat, _miniStream)
            : ReadChain(entry.StartSector, (int)entry.Size, _sectorSize, _fat);
    }

    /// <summary>The paths of the streams directly inside a storage.</summary>
    /// <param name="prefix">Path of the storage, or the empty string for the root.</param>
    public IEnumerable<string> ChildrenOf(string prefix)
    {
        string full = prefix.Length == 0 ? string.Empty : prefix + "/";
        return _byPath.Keys
            .Where(path => path.Length > full.Length &&
                           path.StartsWith(full, StringComparison.OrdinalIgnoreCase) &&
                           !path.AsSpan(full.Length).Contains('/'));
    }

    /// <summary>Whether a storage of that path exists.</summary>
    /// <param name="path">Path of the storage.</param>
    public bool HasStorage(string path) => _byPath.TryGetValue(path, out DirectoryEntry? entry) && entry.Kind == 1;

    private ReadOnlySpan<byte> Span(int offset, int length) => _data.AsSpan(offset, length);

    /// <summary>
    /// Gives every entry a path by walking the directory tree from the root, then registers
    /// anything the walk did not reach under its bare name.
    /// </summary>
    private void BuildPaths()
    {
        if (_directory.Count == 0)
            return;

        var reached = new bool[_directory.Count];
        Walk(_directory[0].Child, string.Empty, reached, 0);

        for (int i = 1; i < _directory.Count; i++)
        {
            if (!reached[i])
                _byPath.TryAdd(_directory[i].Name, _directory[i]);
        }
    }

    private void Walk(int index, string prefix, bool[] reached, int depth)
    {
        if (index < 0 || index >= _directory.Count || reached[index] || depth > 32)
            return;

        reached[index] = true;
        DirectoryEntry entry = _directory[index];
        string path = prefix.Length == 0 ? entry.Name : prefix + "/" + entry.Name;
        _byPath.TryAdd(path, entry);

        Walk(entry.Left, prefix, reached, depth);
        Walk(entry.Right, prefix, reached, depth);
        Walk(entry.Child, path, reached, depth + 1);
    }

    private uint[] ReadFat()
    {
        uint fatSectorCount = BinaryPrimitives.ReadUInt32LittleEndian(Span(44, 4));
        var sectors = new List<uint>();
        var seenFatSectors = new HashSet<uint>();
        int headerSectors = (int)Math.Min(109u, fatSectorCount);
        for (int i = 0; i < headerSectors; i++)
            AddFatSector(BinaryPrimitives.ReadUInt32LittleEndian(Span(76 + (i * 4), 4)));

        // Beyond 109 sectors the table continues through the difat chain.
        uint difat = BinaryPrimitives.ReadUInt32LittleEndian(Span(68, 4));
        uint difatSectorCount = BinaryPrimitives.ReadUInt32LittleEndian(Span(72, 4));
        var seenDifatSectors = new HashSet<uint>();
        uint visitedDifatSectors = 0;
        while (difat is not (EndOfChain or FreeSector) &&
               visitedDifatSectors++ < difatSectorCount &&
               seenDifatSectors.Add(difat) &&
               sectors.Count < fatSectorCount)
        {
            int offset = SectorOffset(difat);
            if (offset + _sectorSize > _data.Length)
                break;

            int entries = (_sectorSize / 4) - 1;
            for (int i = 0; i < entries; i++)
            {
                uint sector = BinaryPrimitives.ReadUInt32LittleEndian(Span(offset + (i * 4), 4));
                AddFatSector(sector);
                if (sectors.Count >= fatSectorCount)
                    break;
            }

            difat = BinaryPrimitives.ReadUInt32LittleEndian(Span(offset + (entries * 4), 4));
        }

        var fat = new List<uint>();
        foreach (uint sector in sectors)
        {
            int offset = SectorOffset(sector);
            if (offset < 0 || offset + _sectorSize > _data.Length)
                continue;
            for (int i = 0; i < _sectorSize / 4; i++)
                fat.Add(BinaryPrimitives.ReadUInt32LittleEndian(Span(offset + (i * 4), 4)));
        }

        return [.. fat];

        void AddFatSector(uint sector)
        {
            int offset = SectorOffset(sector);
            if (offset >= 0 && offset + _sectorSize <= _data.Length && seenFatSectors.Add(sector))
                sectors.Add(sector);
        }
    }

    private uint[] ReadChainValues(uint start)
    {
        var values = new List<uint>();
        foreach (uint sector in Chain(start, _fat))
        {
            int offset = SectorOffset(sector);
            if (offset < 0 || offset + _sectorSize > _data.Length)
                break;
            for (int i = 0; i < _sectorSize / 4; i++)
                values.Add(BinaryPrimitives.ReadUInt32LittleEndian(Span(offset + (i * 4), 4)));
        }

        return [.. values];
    }

    private void ReadDirectory(uint start)
    {
        foreach (uint sector in Chain(start, _fat))
        {
            int offset = SectorOffset(sector);
            if (offset < 0 || offset + _sectorSize > _data.Length)
                break;

            for (int i = 0; i + DirectoryEntrySize <= _sectorSize; i += DirectoryEntrySize)
            {
                DocumentLoadBudgetState.Ensure(
                    nameof(DocumentLoadBudget.MaxPackageParts),
                    _budget.MaxPackageParts,
                    _directory.Count + 1L);
                ReadOnlySpan<byte> raw = Span(offset + i, DirectoryEntrySize);
                int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(raw[64..]);
                if (nameLength is < 2 or > 64)
                {
                    _directory.Add(DirectoryEntry.Unused);
                    continue;
                }

                _directory.Add(new DirectoryEntry(
                    Encoding.Unicode.GetString(raw[..(nameLength - 2)]),
                    raw[66],
                    Link(raw[68..]),
                    Link(raw[72..]),
                    Link(raw[76..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(raw[116..]),
                    StreamSize(raw[120..])));
            }
        }
    }

    private void ValidateDirectory()
    {
        DocumentLoadBudgetState.Ensure(
            nameof(DocumentLoadBudget.MaxPackageParts), _budget.MaxPackageParts, _directory.Count);

        long total = 0;
        for (int index = 0; index < _directory.Count; index++)
        {
            DirectoryEntry entry = _directory[index];
            if (!entry.IsStream && index != 0)
                continue;

            long size = entry.Size > long.MaxValue ? long.MaxValue : (long)entry.Size;
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxPartBytes), _budget.MaxPartBytes, size);
            total = size > long.MaxValue - total ? long.MaxValue : total + size;
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxInflatedBytes), _budget.MaxInflatedBytes, total);
        }
    }

    private static int Link(ReadOnlySpan<byte> raw)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        return value >= MaxRegularSector ? NoEntry : (int)value;
    }

    /// <summary>
    /// The length of a stream, from the eight bytes the entry gives it ([MS-CFB] 2.6.3).
    /// </summary>
    /// <param name="raw">The entry, positioned on its size field.</param>
    /// <remarks>
    /// A version 3 file is capped at two gigabytes and the top half of the field is required
    /// to be zero, but older writers left it uninitialised. The specification asks readers to
    /// ignore it there, and doing so matters: a small stream whose size came back enormous
    /// would be looked for in the wrong allocation and read as somebody else's bytes.
    /// </remarks>
    private ulong StreamSize(ReadOnlySpan<byte> raw)
    {
        ulong size = BinaryPrimitives.ReadUInt64LittleEndian(raw);
        return _isVersion3 ? size & 0xFFFFFFFF : size;
    }

    private byte[] ReadChain(uint start, int size, int sectorSize, uint[] table, byte[]? source = null)
    {
        byte[] container = source ?? _data;
        var result = new byte[size];
        int written = 0;

        foreach (uint sector in Chain(start, table))
        {
            if (written >= size)
                break;

            int offset = source is null ? SectorOffset(sector) : (int)sector * sectorSize;
            if (offset < 0 || offset >= container.Length)
                break;

            int take = Math.Min(sectorSize, Math.Min(size - written, container.Length - offset));
            container.AsSpan(offset, take).CopyTo(result.AsSpan(written));
            written += take;
        }

        return result;
    }

    /// <summary>
    /// Walks a chain of sectors from its start. A valid chain visits no sector twice
    /// ([MS-CFB] 2.1), so one longer than the table it indexes into has a cycle and is cut off.
    /// </summary>
    /// <param name="start">Sector the chain begins at.</param>
    /// <param name="table">The allocation table the chain is threaded through.</param>
    private static IEnumerable<uint> Chain(uint start, uint[] table)
    {
        uint current = start;
        var visited = new HashSet<uint>();
        while (current < table.Length &&
               current is not (EndOfChain or FreeSector) &&
               visited.Add(current))
        {
            yield return current;
            current = table[current];
        }
    }

    /// <summary>
    /// Where a sector begins in the file: <c>(number + 1) × sector size</c> ([MS-CFB] 2.3).
    /// </summary>
    /// <param name="sector">The sector number, or one of the markers that is not one.</param>
    /// <remarks>
    /// Sector zero starts one whole sector in, not 512 bytes in. The two are the same only for
    /// a version 3 file; a version 4 file has 4096-byte sectors and pads its 512-byte header
    /// out to fill the first of them.
    /// </remarks>
    private int SectorOffset(uint sector)
    {
        if (sector > MaxRegularSector)
            return -1;

        long offset = ((long)sector + 1) * _sectorSize;
        return offset > int.MaxValue ? -1 : (int)offset;
    }

    private sealed record DirectoryEntry(
        string Name,
        byte Kind,
        int Left,
        int Right,
        int Child,
        uint StartSector,
        ulong Size)
    {
        /// <summary>A slot of the directory that holds nothing.</summary>
        public static DirectoryEntry Unused { get; } = new(string.Empty, 0, NoEntry, NoEntry, NoEntry, 0, 0);

        public bool IsStream => Kind == 2;
    }
}
