using System.Buffers.Binary;
using System.Text;

namespace Quillwright.IO;

internal sealed partial class CompoundFileWriter
{
    /// <summary>
    /// Works out where every sector goes before a byte is written: which streams are packed
    /// into the mini allocation, where each one starts, and how many sectors the allocation
    /// tables themselves need.
    /// </summary>
    private sealed class Layout
    {
        private readonly List<Entry> _entries = [];
        private readonly byte[] _miniStream;
        private readonly uint[] _fat;
        private readonly uint[] _miniFat;

        public Layout(List<(string Name, byte[] Content)> streams)
        {
            // Small streams are packed into the mini stream first, because the mini stream is
            // itself a regular stream and has to be allocated alongside the big ones.
            var miniBuffer = new List<byte>();
            var placed = new List<Entry>();

            foreach ((string name, byte[] content) in streams)
            {
                var entry = new Entry(name, content, content.Length < MiniStreamCutoff);
                if (entry.IsMini)
                {
                    entry.Start = miniBuffer.Count / MiniSectorSize;
                    miniBuffer.AddRange(content);
                    while (miniBuffer.Count % MiniSectorSize != 0)
                        miniBuffer.Add(0);
                }

                placed.Add(entry);
            }

            _miniStream = [.. miniBuffer];
            _entries.AddRange(placed);

            int miniSectorCount = _miniStream.Length / MiniSectorSize;
            MiniFatSectorCount = miniSectorCount == 0 ? 0 : Ceiling(miniSectorCount, FatEntriesPerSector);
            DirectorySectorCount = Ceiling(_entries.Count + 1, SectorSize / DirectoryEntrySize);

            int cursor = 0;
            foreach (Entry entry in _entries.Where(static e => !e.IsMini && e.Content.Length > 0))
            {
                entry.Start = cursor;
                cursor += Ceiling(entry.Content.Length, SectorSize);
            }

            MiniStreamStart = _miniStream.Length == 0 ? -1 : cursor;
            cursor += Ceiling(_miniStream.Length, SectorSize);
            MiniFatStart = cursor;
            cursor += MiniFatSectorCount;
            DirectoryStart = cursor;
            cursor += DirectorySectorCount;

            (FatSectorCount, DifatSectorCount) = CountFatSectors(cursor);
            FatStart = cursor;
            DifatStart = cursor + FatSectorCount;
            TotalSectors = cursor + FatSectorCount + DifatSectorCount;

            _fat = BuildFat();
            _miniFat = BuildMiniFat(miniSectorCount);
        }

        public int TotalSectors { get; }

        public int FatStart { get; }

        public int FatSectorCount { get; }

        public int DifatStart { get; }

        public int DifatSectorCount { get; }

        public int DirectoryStart { get; }

        public int MiniFatStart { get; }

        public int MiniFatSectorCount { get; }

        private int DirectorySectorCount { get; }

        private int MiniStreamStart { get; }

        public void WriteStreamData(byte[] file)
        {
            foreach (Entry entry in _entries.Where(static e => !e.IsMini && e.Content.Length > 0))
                entry.Content.CopyTo(file.AsSpan(SectorOffset(entry.Start)));

            if (MiniStreamStart >= 0)
                _miniStream.CopyTo(file.AsSpan(SectorOffset(MiniStreamStart)));
        }

        public void WriteMiniFat(byte[] file) => WriteTable(file, MiniFatStart, MiniFatSectorCount, _miniFat);

        public void WriteFat(byte[] file) => WriteTable(file, FatStart, FatSectorCount, _fat);

        public void WriteDifat(byte[] file)
        {
            // The header holds the first 109 allocation-table sectors; anything beyond that
            // continues in a chain of sectors that each end with a pointer to the next.
            for (int i = 0; i < DifatSectorCount; i++)
            {
                Span<byte> sector = file.AsSpan(SectorOffset(DifatStart + i), SectorSize);
                for (int slot = 0; slot < FatEntriesPerSector - 1; slot++)
                {
                    int index = 109 + (i * (FatEntriesPerSector - 1)) + slot;
                    uint value = index < FatSectorCount ? (uint)(FatStart + index) : FreeSector;
                    BinaryPrimitives.WriteUInt32LittleEndian(sector[(slot * 4)..], value);
                }

                uint next = i + 1 < DifatSectorCount ? (uint)(DifatStart + i + 1) : EndOfChain;
                BinaryPrimitives.WriteUInt32LittleEndian(sector[((FatEntriesPerSector - 1) * 4)..], next);
            }
        }

        public void WriteDirectory(byte[] file)
        {
            Span<byte> directory = file.AsSpan(SectorOffset(DirectoryStart), DirectorySectorCount * SectorSize);
            directory.Fill(0);

            // An entry that names nothing is all zeroes but for its three tree pointers, which
            // must say "no such entry" rather than "entry zero" ([MS-CFB] 2.6.3).
            for (int i = 0; i * DirectoryEntrySize < directory.Length; i++)
            {
                Span<byte> entry = directory[(i * DirectoryEntrySize)..];
                BinaryPrimitives.WriteUInt32LittleEndian(entry[68..], NoStream);
                BinaryPrimitives.WriteUInt32LittleEndian(entry[72..], NoStream);
                BinaryPrimitives.WriteUInt32LittleEndian(entry[76..], NoStream);
            }

            WriteEntry(directory, 0, "Root Entry", kind: 5, MiniStreamStart, _miniStream.Length, BuildTree());
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                WriteEntry(directory, i + 1, entry.Name, kind: 2, entry.Content.Length == 0 ? -1 : entry.Start, entry.Content.Length, child: -1);
            }
        }

        /// <summary>
        /// Links the stream entries into the tree the directory is meant to be, ordered the
        /// way [MS-CFB] compares names: by length first, then by upper-cased code unit.
        /// Every node is black, which is a valid colouring for the balanced shape built here.
        /// </summary>
        private int BuildTree()
        {
            int[] sorted = [.. Enumerable.Range(0, _entries.Count)
                .OrderBy(i => _entries[i].Name.Length)
                .ThenBy(i => _entries[i].Name, StringComparer.OrdinalIgnoreCase)];
            return Link(sorted, 0, sorted.Length - 1);
        }

        private int Link(int[] sorted, int low, int high)
        {
            if (low > high)
                return -1;

            int middle = (low + high) / 2;
            Entry node = _entries[sorted[middle]];
            node.Left = Link(sorted, low, middle - 1);
            node.Right = Link(sorted, middle + 1, high);
            return sorted[middle];
        }

        private void WriteEntry(Span<byte> directory, int index, string name, byte kind, int start, long size, int child)
        {
            Span<byte> entry = directory.Slice(index * DirectoryEntrySize, DirectoryEntrySize);
            int bytes = Encoding.Unicode.GetBytes(name, entry[..62]);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[64..], (ushort)(bytes + 2));
            entry[66] = kind;
            entry[67] = 1;

            Entry? node = index == 0 ? null : _entries[index - 1];
            BinaryPrimitives.WriteUInt32LittleEndian(entry[68..], Sibling(node?.Left));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[72..], Sibling(node?.Right));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[76..], child < 0 ? NoStream : (uint)(child + 1));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[116..], start < 0 ? EndOfChain : (uint)start);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[120..], (ulong)size);
        }

        private static uint Sibling(int? index) => index is null or < 0 ? NoStream : (uint)(index.Value + 1);

        private uint[] BuildFat()
        {
            var fat = new uint[FatSectorCount * FatEntriesPerSector];
            Array.Fill(fat, FreeSector);

            foreach (Entry entry in _entries.Where(static e => !e.IsMini && e.Content.Length > 0))
                Chain(fat, entry.Start, Ceiling(entry.Content.Length, SectorSize));

            if (MiniStreamStart >= 0)
                Chain(fat, MiniStreamStart, Ceiling(_miniStream.Length, SectorSize));

            Chain(fat, MiniFatStart, MiniFatSectorCount);
            Chain(fat, DirectoryStart, DirectorySectorCount);

            for (int i = 0; i < FatSectorCount; i++)
                fat[FatStart + i] = FatSector;
            for (int i = 0; i < DifatSectorCount; i++)
                fat[DifatStart + i] = DifatSector;

            return fat;
        }

        private uint[] BuildMiniFat(int miniSectorCount)
        {
            var table = new uint[MiniFatSectorCount * FatEntriesPerSector];
            Array.Fill(table, FreeSector);

            foreach (Entry entry in _entries.Where(static e => e.IsMini && e.Content.Length > 0))
                Chain(table, entry.Start, Ceiling(entry.Content.Length, MiniSectorSize));

            _ = miniSectorCount;
            return table;
        }

        private static void Chain(uint[] table, int start, int count)
        {
            for (int i = 0; i < count; i++)
                table[start + i] = i + 1 < count ? (uint)(start + i + 1) : EndOfChain;
        }

        private static void WriteTable(byte[] file, int start, int sectorCount, uint[] table)
        {
            for (int i = 0; i < sectorCount * FatEntriesPerSector; i++)
            {
                uint value = i < table.Length ? table[i] : FreeSector;
                BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(SectorOffset(start) + (i * 4)), value);
            }
        }

        /// <summary>
        /// The allocation table has to cover the sectors it is stored in, so its size feeds
        /// back into the total. Two or three rounds settle it.
        /// </summary>
        private static (int Fat, int Difat) CountFatSectors(int dataSectors)
        {
            int fat = 0;
            int difat = 0;
            for (int round = 0; round < 8; round++)
            {
                int nextFat = Ceiling(dataSectors + fat + difat, FatEntriesPerSector);
                int nextDifat = nextFat <= 109 ? 0 : Ceiling(nextFat - 109, FatEntriesPerSector - 1);
                if (nextFat == fat && nextDifat == difat)
                    break;
                (fat, difat) = (nextFat, nextDifat);
            }

            return (fat, difat);
        }

        private static int Ceiling(int value, int unit) => value <= 0 ? 0 : ((value - 1) / unit) + 1;

        private static int SectorOffset(int sector) => SectorSize + (sector * SectorSize);

        private sealed class Entry(string name, byte[] content, bool isMini)
        {
            public string Name { get; } = name;

            public byte[] Content { get; } = content;

            public bool IsMini { get; } = isMini;

            public int Start { get; set; }

            public int Left { get; set; } = -1;

            public int Right { get; set; } = -1;
        }
    }
}
