using System.Buffers.Binary;
using System.Collections.Frozen;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Writes the tables that locate fields and bookmarks in the text ([MS-DOC] 2.8.25
/// <c>Plcfld</c>, 2.8.10 <c>Plcfbkf</c> and 2.8.11 <c>Plcfbkl</c>).
/// </summary>
/// <remarks>
/// A field is three characters in the text — begin, separator, end — and this table is what
/// says they belong together and what kind of field they make. The kind is a number rather
/// than the keyword, so the instruction text has to be read to work out which number to
/// write; an unrecognised keyword is written as a general-purpose field, which Word will
/// re-evaluate from the instruction anyway.
/// </remarks>
internal static class FieldTable
{
    private static readonly FrozenDictionary<string, byte> Types = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
    {
        ["REF"] = 0x03, ["FTNREF"] = 0x05, ["SET"] = 0x06, ["IF"] = 0x07, ["INDEX"] = 0x08,
        ["STYLEREF"] = 0x0A, ["SEQ"] = 0x0C, ["TOC"] = 0x0D, ["INFO"] = 0x0E, ["TITLE"] = 0x0F,
        ["SUBJECT"] = 0x10, ["AUTHOR"] = 0x11, ["KEYWORDS"] = 0x12, ["COMMENTS"] = 0x13,
        ["LASTSAVEDBY"] = 0x14, ["CREATEDATE"] = 0x15, ["SAVEDATE"] = 0x16, ["PRINTDATE"] = 0x17,
        ["REVNUM"] = 0x18, ["EDITTIME"] = 0x19, ["NUMPAGES"] = 0x1A, ["NUMWORDS"] = 0x1B,
        ["NUMCHARS"] = 0x1C, ["FILENAME"] = 0x1D, ["TEMPLATE"] = 0x1E, ["DATE"] = 0x1F,
        ["TIME"] = 0x20, ["PAGE"] = 0x21, ["QUOTE"] = 0x23, ["INCLUDE"] = 0x24, ["PAGEREF"] = 0x25,
        ["ASK"] = 0x26, ["FILLIN"] = 0x27, ["DATA"] = 0x28, ["NEXT"] = 0x29, ["NEXTIF"] = 0x2A,
        ["SKIPIF"] = 0x2B, ["MERGEREC"] = 0x2C, ["DDE"] = 0x2D, ["DDEAUTO"] = 0x2E,
        ["GLOSSARY"] = 0x2F, ["PRINT"] = 0x30, ["EQ"] = 0x31, ["GOTOBUTTON"] = 0x32,
        ["MACROBUTTON"] = 0x33, ["AUTONUMOUT"] = 0x34, ["AUTONUMLGL"] = 0x35, ["AUTONUM"] = 0x36,
        ["IMPORT"] = 0x37, ["LINK"] = 0x38, ["SYMBOL"] = 0x39, ["EMBED"] = 0x3A,
        ["MERGEFIELD"] = 0x3B, ["USERNAME"] = 0x3C, ["USERINITIALS"] = 0x3D, ["USERADDRESS"] = 0x3E,
        ["BARCODE"] = 0x3F, ["DOCVARIABLE"] = 0x40, ["SECTION"] = 0x41, ["SECTIONPAGES"] = 0x42,
        ["INCLUDEPICTURE"] = 0x43, ["INCLUDETEXT"] = 0x44, ["FILESIZE"] = 0x45, ["FORMTEXT"] = 0x46,
        ["FORMCHECKBOX"] = 0x47, ["NOTEREF"] = 0x48, ["TOA"] = 0x49, ["MERGESEQ"] = 0x4B,
        ["AUTOTEXT"] = 0x4F, ["COMPARE"] = 0x50, ["ADDIN"] = 0x51, ["FORMDROPDOWN"] = 0x53,
        ["ADVANCE"] = 0x54, ["DOCPROPERTY"] = 0x55, ["CONTROL"] = 0x57, ["HYPERLINK"] = 0x58,
        ["AUTOTEXTLIST"] = 0x59, ["LISTNUM"] = 0x5A, ["HTMLCONTROL"] = 0x5B, ["BIDIOUTLINE"] = 0x5C,
        ["ADDRESSBLOCK"] = 0x5D, ["GREETINGLINE"] = 0x5E, ["SHAPE"] = 0x5F,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The number that stands for a field keyword, or the general-purpose one.</summary>
    /// <param name="instruction">The field instruction, of which only the first word matters.</param>
    public static byte TypeOf(string instruction)
    {
        ReadOnlySpan<char> text = instruction.AsSpan().TrimStart();
        int end = text.IndexOfAny(' ', '\t');
        ReadOnlySpan<char> keyword = end < 0 ? text : text[..end];
        return Types.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(keyword, out byte type) ? type : (byte)0x00;
    }

    /// <summary>Writes the field and bookmark tables of every story.</summary>
    public static void Write(DocWriteContext context, StoryAssembler story, FibBuilder fib, Action<int, byte[]> add)
    {
        _ = fib;
        string text = story.Text;
        add(FibBuilder.Pair.MainFields, Fields(context.Fields, text, 0, story.MainLength));

        int footnoteStart = story.MainLength;
        add(FibBuilder.Pair.FootnoteFields, Fields(context.Fields, text, footnoteStart, story.FootnoteLength));

        int headerStart = footnoteStart + story.FootnoteLength;
        add(FibBuilder.Pair.HeaderFields, Fields(context.Fields, text, headerStart, story.HeaderLength));

        int commentStart = headerStart + story.HeaderLength;
        add(FibBuilder.Pair.CommentFields, Fields(context.Fields, text, commentStart, story.CommentLength));

        WriteBookmarks(context, add);
    }

    /// <summary>
    /// The instruction of the field that begins at a position: everything up to the
    /// separator, or up to the end when the field has no result.
    /// </summary>
    private static string InstructionAt(string text, int begin)
    {
        for (int i = begin + 1; i < text.Length; i++)
        {
            if (text[i] is DocChar.FieldSeparator or DocChar.FieldEnd)
                return text[(begin + 1)..i];
            if (text[i] == DocChar.FieldBegin)
                break;
        }

        return string.Empty;
    }

    /// <summary>
    /// Builds one story's field list. Positions are counted from the start of the story
    /// rather than of the document, so each story's fields are gathered separately.
    /// </summary>
    private static byte[] Fields(List<FieldSpan> fields, string text, int storyStart, int storyLength)
    {
        if (storyLength == 0)
            return [];

        List<FieldSpan> owned =
        [
            .. fields.Where(f => f.Position >= storyStart && f.Position < storyStart + storyLength)
                     .OrderBy(static f => f.Position),
        ];

        if (owned.Count == 0)
            return [];

        var builder = new PlcBuilder(2);
        Span<byte> record = stackalloc byte[2];
        for (int i = 0; i < owned.Count; i++)
        {
            record[0] = owned[i].Kind switch
            {
                Model.FieldCharKind.Separate => 0x14,
                Model.FieldCharKind.End => 0x15,
                _ => 0x13,
            };

            // Only a begin character carries the field's kind, and the kind is whatever the
            // instruction that follows it turns out to say.
            record[1] = record[0] == 0x13 ? TypeOf(InstructionAt(text, owned[i].Position)) : (byte)0;

            int from = owned[i].Position - storyStart;
            int to = i + 1 < owned.Count ? owned[i + 1].Position - storyStart : from + 1;
            builder.Add(from, to, record);
        }

        return builder.ToArray();
    }

    /// <summary>
    /// Writes the bookmarks as three parallel structures: the names, where each one opens,
    /// and where each one closes. The opening record points at the closing one by index,
    /// which is what lets bookmarks overlap.
    /// </summary>
    private static void WriteBookmarks(DocWriteContext context, Action<int, byte[]> add)
    {
        List<BookmarkSpan> bookmarks = [.. context.Bookmarks.OrderBy(static b => b.StartPosition)];
        if (bookmarks.Count == 0)
            return;

        int[] byEnd = [.. Enumerable.Range(0, bookmarks.Count).OrderBy(i => bookmarks[i].EndPosition)];
        var order = new int[bookmarks.Count];
        for (int i = 0; i < byEnd.Length; i++)
            order[byEnd[i]] = i;

        var starts = new PlcBuilder(4);
        Span<byte> record = stackalloc byte[4];
        for (int i = 0; i < bookmarks.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(record, (ushort)order[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(record[2..], 0);
            int to = i + 1 < bookmarks.Count ? bookmarks[i + 1].StartPosition : bookmarks[i].StartPosition + 1;
            starts.Add(bookmarks[i].StartPosition, to, record);
        }

        List<int> ends = [.. byEnd.Select(i => bookmarks[i].EndPosition)];
        ends.Add(ends.Count == 0 ? 0 : ends[^1] + 1);

        add(FibBuilder.Pair.BookmarkNames, PlcBuilder.StringTable([.. bookmarks.Select(static b => b.Name)]));
        add(FibBuilder.Pair.BookmarkStarts, starts.ToArray());
        add(FibBuilder.Pair.BookmarkEnds, PlcBuilder.Positions(ends));
    }
}
