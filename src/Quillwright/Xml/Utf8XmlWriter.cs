using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text.Unicode;

namespace Quillwright.Xml;

/// <summary>
/// Minimal forward-only XML writer that emits UTF-8 bytes directly into a pooled buffer.
/// Element structure is the caller's responsibility (via <see cref="WriteRaw"/> of u8 literals);
/// this type owns correct text escaping, number formatting and buffered async flushing.
/// </summary>
internal sealed class Utf8XmlWriter : IAsyncDisposable
{
    private const int DefaultBufferSize = 64 * 1024;
    private const int FlushThreshold = 48 * 1024;

    // '&', '<', '>', '"' plus every XML 1.0-invalid control character.
    private static readonly SearchValues<char> NeedsEscape = SearchValues.Create(
        "&<>\"\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000B\u000C\u000E\u000F" +
        "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F" +
        "\t\n\r");

    private readonly Stream _stream;
    private byte[] _buffer;
    private int _position;

    public Utf8XmlWriter(Stream stream, int bufferSize = DefaultBufferSize)
    {
        _stream = stream;
        _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
    }

    /// <summary>
    /// Whether the part being written belongs to a Strict package, which spells a handful of
    /// names and values differently.
    /// </summary>
    /// <remarks>
    /// The two vocabularies differ in more than their namespace: Strict renamed the direction
    /// words that assume a left-to-right page, so a table border is <c>w:start</c> rather than
    /// <c>w:left</c> and an alignment is <c>start</c> rather than <c>left</c>. Emitting the
    /// Transitional spelling under the Strict namespace produces a part no schema accepts, so
    /// the flag rides on the writer — every part writer already has one, and none of them
    /// would otherwise know which package it is building.
    /// </remarks>
    public bool Strict { get; set; }

    /// <summary>
    /// Renders markup to a string, for the few places that need a fragment as text rather
    /// than as bytes in a part — a formatting change record, which is stored on the format it
    /// belongs to.
    /// </summary>
    /// <param name="write">Writes the fragment.</param>
    public static string Render(Action<Utf8XmlWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        using var buffer = new MemoryStream();
        var writer = new Utf8XmlWriter(buffer, 4096);
        try
        {
            write(writer);
            writer.Flush();
        }
        finally
        {
            writer.Release();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Writes the standard WordprocessingML XML declaration.</summary>
    public void WriteDeclaration() =>
        WriteRaw("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n"u8);

    /// <summary>Writes pre-encoded UTF-8 markup verbatim.</summary>
    public void WriteRaw(scoped ReadOnlySpan<byte> utf8)
    {
        EnsureCapacity(utf8.Length);
        utf8.CopyTo(_buffer.AsSpan(_position));
        _position += utf8.Length;
    }

    /// <summary>
    /// Writes already-formed markup, transcoding UTF-16 to UTF-8 without escaping. Used to
    /// re-emit parts of a document that were captured verbatim while reading.
    /// </summary>
    public void WriteRawXml(scoped ReadOnlySpan<char> xml) => TranscodeChunk(xml);

    /// <summary>Writes escaped element text.</summary>
    public void WriteText(scoped ReadOnlySpan<char> text) => WriteEscaped(text, attribute: false);

    /// <summary>Writes an escaped attribute value (quotes and whitespace get character references).</summary>
    public void WriteAttributeText(scoped ReadOnlySpan<char> text) => WriteEscaped(text, attribute: true);

    /// <summary>Writes an integer in invariant culture.</summary>
    public void WriteInt32(int value)
    {
        EnsureCapacity(11);
        Utf8Formatter.TryFormat(value, _buffer.AsSpan(_position), out int written);
        _position += written;
    }

    /// <summary>Writes a 64-bit integer in invariant culture.</summary>
    public void WriteInt64(long value)
    {
        EnsureCapacity(20);
        Utf8Formatter.TryFormat(value, _buffer.AsSpan(_position), out int written);
        _position += written;
    }

    /// <summary>Writes a number using the shortest round-trippable invariant representation.</summary>
    public void WriteDouble(double value)
    {
        EnsureCapacity(32);
        value.TryFormat(_buffer.AsSpan(_position), out int written, default, CultureInfo.InvariantCulture);
        _position += written;
    }

    /// <summary>Writes any UTF-8 formattable value, e.g. a <see cref="Primitives.Length"/>.</summary>
    public void WriteFormattable<T>(T value) where T : IUtf8SpanFormattable
    {
        EnsureCapacity(32);
        if (!value.TryFormat(_buffer.AsSpan(_position), out int written, default, CultureInfo.InvariantCulture))
        {
            EnsureCapacity(256);
            value.TryFormat(_buffer.AsSpan(_position), out written, default, CultureInfo.InvariantCulture);
        }

        _position += written;
    }

    /// <summary>Flushes the buffer to the underlying stream when it crossed the threshold.</summary>
    public ValueTask FlushIfNeededAsync(CancellationToken cancellationToken) =>
        _position >= FlushThreshold ? FlushAsync(cancellationToken) : ValueTask.CompletedTask;

    /// <summary>Flushes all buffered bytes to the underlying stream.</summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        if (_position > 0)
        {
            await _stream.WriteAsync(_buffer.AsMemory(0, _position), cancellationToken).ConfigureAwait(false);
            _position = 0;
        }
    }

    /// <summary>Writes the buffered bytes to the stream without waiting.</summary>
    public void Flush()
    {
        if (_position == 0)
            return;

        _stream.Write(_buffer, 0, _position);
        _position = 0;
    }

    /// <summary>Returns the pooled buffer, leaving the stream alone.</summary>
    public void Release()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    /// <summary>Flushes remaining bytes, returns the pooled buffer and disposes the underlying stream.</summary>
    public async ValueTask DisposeAsync()
    {
        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        Release();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    private void WriteEscaped(scoped ReadOnlySpan<char> text, bool attribute)
    {
        while (!text.IsEmpty)
        {
            int special = text.IndexOfAny(NeedsEscape);
            if (special < 0)
            {
                TranscodeChunk(text);
                return;
            }

            if (special > 0)
                TranscodeChunk(text[..special]);

            WriteSpecial(text[special], attribute);
            text = text[(special + 1)..];
        }
    }

    private void WriteSpecial(char c, bool attribute)
    {
        switch (c)
        {
            case '&':
                WriteRaw("&amp;"u8);
                break;
            case '<':
                WriteRaw("&lt;"u8);
                break;
            case '>':
                WriteRaw("&gt;"u8);
                break;
            case '"':
                if (attribute) WriteRaw("&quot;"u8); else WriteByte((byte)'"');
                break;
            case '\t' or '\n' or '\r':
                if (attribute)
                    WriteRaw(c switch { '\t' => "&#9;"u8, '\n' => "&#10;"u8, _ => "&#13;"u8 });
                else
                    WriteByte((byte)c);
                break;
            default:
                // XML 1.0 has no representation for these at all, not even a character
                // reference. Word drops them, and so do we, rather than emit a file no
                // parser will read back.
                break;
        }
    }

    private void TranscodeChunk(scoped ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            EnsureCapacity(Math.Min(text.Length * 3, 1024));
            Utf8.FromUtf16(text, _buffer.AsSpan(_position), out int charsRead, out int bytesWritten,
                replaceInvalidSequences: true, isFinalBlock: true);
            _position += bytesWritten;
            text = text[charsRead..];
        }
    }

    private void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

    private void EnsureCapacity(int needed)
    {
        if (_buffer.Length - _position >= needed)
            return;

        byte[] bigger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _position + needed));
        _buffer.AsSpan(0, _position).CopyTo(bigger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = bigger;
    }
}
