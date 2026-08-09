using System.Buffers.Binary;
using System.Text;
using Quillwright.Diagnostics;

namespace Quillwright.IO;

/// <summary>
/// What the streams of an embedded object's storage say about it ([MS-OLEDS] 1.3.3): what it
/// calls itself, whether it is linked, and the file it wraps when it wraps one.
/// </summary>
/// <param name="DisplayName">The phrase a user sees, from <c>\1CompObj</c>.</param>
/// <param name="ProgramId">The program that owns the object, when the container names one.</param>
/// <param name="IsLinked">Whether the storage stands for a link rather than an embedding.</param>
/// <param name="PackagedFileName">Name of the plain file the object wraps, when it wraps one.</param>
/// <param name="PackagedFile">That file's own bytes.</param>
internal readonly record struct OleDescription(
    string? DisplayName,
    string? ProgramId,
    bool IsLinked,
    string? PackagedFileName,
    byte[]? PackagedFile);

/// <summary>
/// Reads the bookkeeping streams of an embedded OLE object ([MS-OLEDS] 2.3).
/// </summary>
/// <remarks>
/// An embedded object is a compound file of its own. Three streams describe it whatever it
/// holds: <c>\1Ole</c> says whether it is linked, <c>\1CompObj</c> gives it a name, and
/// <c>\1Ole10Native</c> carries the data of an object converted from the older format. The
/// object's real content sits beside them under whatever names its own program chose, and is
/// not interpreted here.
/// </remarks>
internal static class OleContainer
{
    private const string OleStream = "\u0001Ole";
    private const string CompObjStream = "\u0001CompObj";
    private const string NativeStream = "\u0001Ole10Native";
    private const int CompObjHeaderBytes = 28;
    private const uint UnicodeMarker = 0x71B239F4;

    /// <summary>Reads what the container says about itself, or nothing when it is not one.</summary>
    /// <param name="bytes">The whole storage as a compound file.</param>
    /// <param name="budget">Optional limits inherited from the outer document load.</param>
    public static OleDescription? Describe(byte[] bytes, DocumentLoadBudget? budget = null)
    {
        if (!CompoundFile.IsCompoundFile(bytes))
            return null;

        CompoundFile container;
        try
        {
            container = CompoundFile.Open(bytes, budget);
        }
        catch (CompoundFileException)
        {
            return null;
        }

        (string? name, string? programId) = ReadCompObj(container.ReadStream(CompObjStream));
        (string? file, byte[]? content) = ReadPackage(container.ReadStream(NativeStream));
        return new OleDescription(name, programId, IsLinked(container.ReadStream(OleStream)), file, content);
    }

    /// <summary>Whether the <c>\1Ole</c> stream marks the storage as a link ([MS-OLEDS] 2.3.3).</summary>
    private static bool IsLinked(byte[]? stream) =>
        stream is { Length: >= 8 } && (BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(4)) & 1) != 0;

    /// <summary>
    /// The display name and program identifier from <c>\1CompObj</c> ([MS-OLEDS] 2.3.8).
    /// </summary>
    /// <remarks>
    /// The specification reserves the string that follows the clipboard format and says to
    /// ignore it. Every writer puts the program identifier there, so it is read but only
    /// trusted when it looks like one.
    /// </remarks>
    private static (string? DisplayName, string? ProgramId) ReadCompObj(byte[]? stream)
    {
        if (stream is null || stream.Length <= CompObjHeaderBytes)
            return (null, null);

        int at = CompObjHeaderBytes;
        string? ansiName = ReadAnsiString(stream, ref at);
        SkipClipboardFormat(stream, ref at);
        string? reserved = ReadAnsiString(stream, ref at);

        string? unicodeName = null;
        if (at + 4 <= stream.Length && BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(at)) == UnicodeMarker)
        {
            at += 4;
            unicodeName = ReadUnicodeString(stream, ref at);
        }

        return (unicodeName ?? ansiName, LooksLikeProgramId(reserved) ? reserved : null);
    }

    private static bool LooksLikeProgramId(string? value) =>
        value is { Length: > 2 } && value.Contains('.', StringComparison.Ordinal) && !value.Contains(' ', StringComparison.Ordinal);

    /// <summary>
    /// The plain file an object wraps, out of the native data of a packaged object.
    /// </summary>
    /// <remarks>
    /// The layout of that data is the packager's own rather than anything [MS-OLEDS]
    /// specifies, which describes the stream only as an array of bytes. It is read here
    /// because extracting an attachment is the whole point of reaching an embedded object,
    /// and every field is checked so that data of some other shape simply yields nothing.
    /// </remarks>
    private static (string? Name, byte[]? Content) ReadPackage(byte[]? stream)
    {
        if (stream is null || stream.Length < 8)
            return (null, null);

        int size = BinaryPrimitives.ReadInt32LittleEndian(stream);
        if (size < 0 || size + 4 > stream.Length)
            return (null, null);

        int at = 6;
        string? label = ReadTerminated(stream, ref at);
        _ = ReadTerminated(stream, ref at);
        if (label is null || at + 8 > stream.Length)
            return (null, null);

        at += 4;
        int pathLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(at));
        at += 4;
        if (pathLength < 0 || at + pathLength + 4 > stream.Length)
            return (null, null);

        at += pathLength;
        int contentLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(at));
        at += 4;
        return contentLength < 0 || at + contentLength > stream.Length
            ? (null, null)
            : (label, stream.AsSpan(at, contentLength).ToArray());
    }

    /// <summary>Reads a length-prefixed single-byte string ([MS-OLEDS] 2.1.4).</summary>
    private static string? ReadAnsiString(byte[] stream, ref int at)
    {
        if (at + 4 > stream.Length)
            return null;

        int length = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(at));
        at += 4;
        if (length is <= 0 or > 0x8000 || at + length > stream.Length)
            return null;

        string text = Encoding.Latin1.GetString(stream, at, length).TrimEnd('\0');
        at += length;
        return text.Length == 0 ? null : text;
    }

    /// <summary>Reads a length-prefixed two-byte string ([MS-OLEDS] 2.1.5).</summary>
    private static string? ReadUnicodeString(byte[] stream, ref int at)
    {
        if (at + 4 > stream.Length)
            return null;

        int characters = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(at));
        at += 4;
        if (characters is <= 0 or > 0x8000 || at + (characters * 2) > stream.Length)
            return null;

        string text = Encoding.Unicode.GetString(stream, at, characters * 2).TrimEnd('\0');
        at += characters * 2;
        return text.Length == 0 ? null : text;
    }

    /// <summary>Steps over a clipboard format, which is either a number or a string ([MS-OLEDS] 2.3.1).</summary>
    private static void SkipClipboardFormat(byte[] stream, ref int at)
    {
        if (at + 4 > stream.Length)
            return;

        uint marker = BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(at));
        at += 4;
        if (marker is 0x00000000)
            return;
        at += marker is 0xFFFFFFFF or 0xFFFFFFFE ? 4 : (int)Math.Min(marker, (uint)(stream.Length - at));
    }

    private static string? ReadTerminated(byte[] stream, ref int at)
    {
        int start = at;
        while (at < stream.Length && stream[at] != 0)
            at++;

        if (at >= stream.Length)
            return null;

        string text = Encoding.Latin1.GetString(stream, start, at - start);
        at++;
        return text.Length == 0 ? null : text;
    }
}
