using System.Buffers;
using System.Globalization;
using System.Text;

namespace Quillwright.Diagnostics;

/// <summary>
/// Bounds the work and memory a document reader may spend on caller-controlled input.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are deliberately generous enough for ordinary office documents while still
/// putting a finite ceiling on compressed packages, markup trees and referenced media. A
/// caller that accepts unusually large trusted files can supply a larger budget through the
/// format's import options.
/// </para>
/// <para>
/// Limits are inclusive: an input exactly at a limit is accepted. Values must be positive.
/// </para>
/// </remarks>
public sealed record DocumentLoadBudget
{
    private const long MiB = 1024L * 1024L;

    /// <summary>The shared default budget.</summary>
    public static DocumentLoadBudget Default { get; } = new();

    /// <summary>Largest source file or stream, in bytes. The default is 128 MiB.</summary>
    public long MaxInputBytes { get; init; } = 128 * MiB;

    /// <summary>Largest number of ZIP parts or compound-file directory entries. The default is 16,384.</summary>
    public int MaxPackageParts { get; init; } = 16_384;

    /// <summary>Largest total uncompressed size of package parts or container streams. The default is 1 GiB.</summary>
    public long MaxInflatedBytes { get; init; } = 1024 * MiB;

    /// <summary>Largest uncompressed package part or compound-file stream. The default is 512 MiB.</summary>
    public long MaxPartBytes { get; init; } = 512 * MiB;

    /// <summary>Largest XML part, measured in decoded characters. The default is 512 Mi characters.</summary>
    public long MaxXmlCharactersPerPart { get; init; } = 512 * MiB;

    /// <summary>Largest total number of XML nodes read from one package. The default is four million.</summary>
    public long MaxXmlNodes { get; init; } = 4_000_000;

    /// <summary>Largest XML element nesting depth. The default is 256.</summary>
    public int MaxXmlDepth { get; init; } = 256;

    /// <summary>Largest decoded HTML, Markdown or RTF text, in UTF-16 code units. The default is 16 million.</summary>
    public int MaxTextCharacters { get; init; } = 16_000_000;

    /// <summary>Largest number of nodes produced by a markup parser. The default is two million.</summary>
    public long MaxMarkupNodes { get; init; } = 2_000_000;

    /// <summary>Largest HTML, Markdown or RTF nesting depth. The default is 256.</summary>
    public int MaxMarkupDepth { get; init; } = 256;

    /// <summary>Largest number of source lines in textual markup. The default is one million.</summary>
    public int MaxLines { get; init; } = 1_000_000;

    /// <summary>Largest decoded image or other media item. The default is 64 MiB.</summary>
    public long MaxMediaBytes { get; init; } = 64 * MiB;

    /// <summary>Largest total decoded media payload. The default is 256 MiB.</summary>
    public long MaxTotalMediaBytes { get; init; } = 256 * MiB;

    /// <summary>Largest embedded object payload. The default is 128 MiB.</summary>
    public long MaxEmbeddedObjectBytes { get; init; } = 128 * MiB;

    /// <summary>Largest number of embedded objects. The default is 4,096.</summary>
    public int MaxEmbeddedObjects { get; init; } = 4_096;

    /// <summary>Checks that every configured limit is positive.</summary>
    public void Validate()
    {
        Positive(MaxInputBytes, nameof(MaxInputBytes));
        Positive(MaxPackageParts, nameof(MaxPackageParts));
        Positive(MaxInflatedBytes, nameof(MaxInflatedBytes));
        Positive(MaxPartBytes, nameof(MaxPartBytes));
        Positive(MaxXmlCharactersPerPart, nameof(MaxXmlCharactersPerPart));
        Positive(MaxXmlNodes, nameof(MaxXmlNodes));
        Positive(MaxXmlDepth, nameof(MaxXmlDepth));
        Positive(MaxTextCharacters, nameof(MaxTextCharacters));
        Positive(MaxMarkupNodes, nameof(MaxMarkupNodes));
        Positive(MaxMarkupDepth, nameof(MaxMarkupDepth));
        Positive(MaxLines, nameof(MaxLines));
        Positive(MaxMediaBytes, nameof(MaxMediaBytes));
        Positive(MaxTotalMediaBytes, nameof(MaxTotalMediaBytes));
        Positive(MaxEmbeddedObjectBytes, nameof(MaxEmbeddedObjectBytes));
        Positive(MaxEmbeddedObjects, nameof(MaxEmbeddedObjects));
    }

    private static void Positive(long value, string name)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(name, value, "A document load limit must be positive.");
    }
}

/// <summary>Thrown when caller-controlled input exceeds a configured <see cref="DocumentLoadBudget"/> limit.</summary>
public sealed class DocumentLoadLimitException : Exception
{
    /// <summary>Creates an exception that names the exceeded property, its limit and the observed value.</summary>
    public DocumentLoadLimitException(string limitName, long limit, long observed)
        : base(FormattableString.Invariant(
            $"Document load limit '{limitName}' exceeded (limit: {limit.ToString(CultureInfo.InvariantCulture)}, observed: {observed.ToString(CultureInfo.InvariantCulture)})."))
    {
        ArgumentException.ThrowIfNullOrEmpty(limitName);
        LimitName = limitName;
        Limit = limit;
        Observed = observed;
    }

    /// <summary>Name of the <see cref="DocumentLoadBudget"/> property that was exceeded.</summary>
    public string LimitName { get; }

    /// <summary>Configured inclusive limit.</summary>
    public long Limit { get; }

    /// <summary>Value observed when the reader stopped.</summary>
    public long Observed { get; }
}

/// <summary>Mutable counters shared by the readers participating in one import.</summary>
internal sealed class DocumentLoadBudgetState
{
    private long _xmlNodes;
    private long _markupNodes;
    private long _mediaBytes;
    private int _embeddedObjects;

    public DocumentLoadBudgetState(DocumentLoadBudget budget)
    {
        Budget = budget ?? throw new ArgumentNullException(nameof(budget));
        budget.Validate();
    }

    public DocumentLoadBudget Budget { get; }

    public long MaximumNextMediaBytes =>
        Math.Min(Budget.MaxMediaBytes, Math.Max(0, Budget.MaxTotalMediaBytes - _mediaBytes));

    public static void Ensure(string limitName, long limit, long observed)
    {
        if (observed > limit)
            throw new DocumentLoadLimitException(limitName, limit, observed);
    }

    public void ValidateText(string text)
    {
        Ensure(nameof(DocumentLoadBudget.MaxTextCharacters), Budget.MaxTextCharacters, text.Length);

        int lines = 1;
        foreach (char character in text)
        {
            if (character == '\n')
            {
                lines++;
                if (lines > Budget.MaxLines)
                    throw new DocumentLoadLimitException(nameof(DocumentLoadBudget.MaxLines), Budget.MaxLines, lines);
            }
        }
    }

    public void AddXmlNode(int depth)
    {
        Ensure(nameof(DocumentLoadBudget.MaxXmlDepth), Budget.MaxXmlDepth, depth);
        _xmlNodes++;
        Ensure(nameof(DocumentLoadBudget.MaxXmlNodes), Budget.MaxXmlNodes, _xmlNodes);
    }

    public void AddMarkupNode(long count = 1)
    {
        _markupNodes = checked(_markupNodes + count);
        Ensure(nameof(DocumentLoadBudget.MaxMarkupNodes), Budget.MaxMarkupNodes, _markupNodes);
    }

    public void EnsureMarkupDepth(int depth) =>
        Ensure(nameof(DocumentLoadBudget.MaxMarkupDepth), Budget.MaxMarkupDepth, depth);

    public void EnsureMedia(long bytes)
    {
        Ensure(nameof(DocumentLoadBudget.MaxMediaBytes), Budget.MaxMediaBytes, bytes);
        long observed = bytes > long.MaxValue - _mediaBytes ? long.MaxValue : _mediaBytes + bytes;
        Ensure(nameof(DocumentLoadBudget.MaxTotalMediaBytes), Budget.MaxTotalMediaBytes, observed);
    }

    public void AddMedia(long bytes)
    {
        EnsureMedia(bytes);
        _mediaBytes += bytes;
    }

    public void AddEmbeddedObject(long bytes)
    {
        Ensure(nameof(DocumentLoadBudget.MaxEmbeddedObjectBytes), Budget.MaxEmbeddedObjectBytes, bytes);
        int count = checked(++_embeddedObjects);
        Ensure(nameof(DocumentLoadBudget.MaxEmbeddedObjects), Budget.MaxEmbeddedObjects, count);
    }
}

/// <summary>Reads caller-controlled sources without unbounded MemoryStream/File helpers.</summary>
internal static class DocumentInput
{
    public static async ValueTask<byte[]> ReadFileBytesAsync(
        string path, DocumentLoadBudget budget, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });
        return await ReadBytesAsync(stream, budget, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<byte[]> ReadBytesAsync(
        Stream stream, DocumentLoadBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Validate();

        if (stream.CanSeek)
        {
            long remaining = Math.Max(0, stream.Length - stream.Position);
            DocumentLoadBudgetState.Ensure(nameof(DocumentLoadBudget.MaxInputBytes), budget.MaxInputBytes, remaining);
        }

        using var content = new MemoryStream((int)Math.Min(budget.MaxInputBytes, 81920));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long total = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return content.ToArray();

                long observed = read > long.MaxValue - total ? long.MaxValue : total + read;
                DocumentLoadBudgetState.Ensure(
                    nameof(DocumentLoadBudget.MaxInputBytes), budget.MaxInputBytes, observed);
                await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total = observed;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async ValueTask<string> ReadUtf8TextFileAsync(
        string path, DocumentLoadBudget budget, CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadFileBytesAsync(path, budget, cancellationToken).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
