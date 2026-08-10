using System.Buffers;
using System.Text;
using Quillwright.Diagnostics;

namespace Quillwright.Html;

/// <summary>Determines and applies an HTML byte stream's character encoding.</summary>
internal static class HtmlEncoding
{
    private const int PrescanByteCount = 1024;

    private enum AttributeRead : byte
    {
        Attribute,
        TagEnd,
        EndOfBytes,
    }

    /// <summary>
    /// Decodes a byte stream using the WHATWG precedence: BOM, the HTML prescan, then UTF-8.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes) =>
        Decode(bytes, int.MaxValue, CancellationToken.None);

    /// <summary>
    /// Decodes a byte stream while bounding the work between cancellation observations.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken) =>
        Decode(bytes, int.MaxValue, cancellationToken);

    /// <summary>
    /// Decodes a byte stream and stops before retaining more than <paramref name="maxCharacters"/>
    /// UTF-16 code units.
    /// </summary>
    public static string Decode(
        ReadOnlySpan<byte> bytes,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 1);
        cancellationToken.ThrowIfCancellationRequested();
        Encoding encoding;
        int preambleLength;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = Encoding.UTF8;
            preambleLength = 3;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            preambleLength = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            preambleLength = 2;
        }
        else
        {
            ReadOnlySpan<byte> prefix = bytes[..Math.Min(bytes.Length, PrescanByteCount)];
            encoding = Prescan(prefix) ?? Encoding.UTF8;
            preambleLength = 0;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return DecodeInChunks(bytes[preambleLength..], encoding, maxCharacters, cancellationToken);
    }

    private static string DecodeInChunks(
        ReadOnlySpan<byte> bytes,
        Encoding encoding,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        const int ByteChunkSize = 32 * 1024;
        if (bytes.IsEmpty)
            return string.Empty;

        int bufferLength = encoding.GetMaxCharCount(Math.Min(bytes.Length, ByteChunkSize));
        char[] characters = ArrayPool<char>.Shared.Rent(bufferLength);
        var decoded = new StringBuilder(Math.Min(Math.Min(bytes.Length, maxCharacters), 1024 * 1024));
        Decoder decoder = encoding.GetDecoder();
        try
        {
            int position = 0;
            while (position < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int byteCount = Math.Min(ByteChunkSize, bytes.Length - position);
                bool flush = position + byteCount == bytes.Length;
                ReadOnlySpan<byte> remaining = bytes.Slice(position, byteCount);
                bool completed;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    decoder.Convert(
                        remaining,
                        characters,
                        flush,
                        out int bytesUsed,
                        out int charactersUsed,
                        out completed);
                    if (charactersUsed > 0)
                    {
                        long observed = (long)decoded.Length + charactersUsed;
                        DocumentLoadBudgetState.Ensure(
                            nameof(DocumentLoadBudget.MaxTextCharacters), maxCharacters, observed);
                        decoded.Append(characters, 0, charactersUsed);
                    }

                    position += bytesUsed;
                    remaining = remaining[bytesUsed..];
                    if (bytesUsed == 0 && charactersUsed == 0 && !completed)
                        throw new InvalidOperationException("The selected HTML decoder made no progress.");
                }
                while (!remaining.IsEmpty || (flush && !completed));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return string.Create(
                decoded.Length,
                (Decoded: decoded, CancellationToken: cancellationToken),
                static (destination, state) =>
                {
                    int position = 0;
                    foreach (ReadOnlyMemory<char> chunk in state.Decoded.GetChunks())
                    {
                        state.CancellationToken.ThrowIfCancellationRequested();
                        chunk.Span.CopyTo(destination[position..]);
                        position += chunk.Length;
                    }

                    state.CancellationToken.ThrowIfCancellationRequested();
                });
        }
        finally
        {
            ArrayPool<char>.Shared.Return(characters);
        }
    }

    private static Encoding? Prescan(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 6 &&
            bytes[0] == 0x3C && bytes[1] == 0x00 && bytes[2] == 0x3F &&
            bytes[3] == 0x00 && bytes[4] == 0x78 && bytes[5] == 0x00)
        {
            return Encoding.Unicode;
        }

        if (bytes.Length >= 6 &&
            bytes[0] == 0x00 && bytes[1] == 0x3C && bytes[2] == 0x00 &&
            bytes[3] == 0x3F && bytes[4] == 0x00 && bytes[5] == 0x78)
        {
            return Encoding.BigEndianUnicode;
        }

        int position = 0;
        while (position < bytes.Length)
        {
            if (StartsWith(bytes, position, "<!--"u8))
            {
                int end = IndexOf(bytes, position + 4, "-->"u8);
                if (end < 0)
                    return XmlEncoding(bytes);

                position = end + 3;
                continue;
            }

            if (IsMetaStart(bytes, position))
            {
                position += 5;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                bool gotPragma = false;
                bool? needPragma = null;
                bool charsetSet = false;
                Encoding? charset = null;

                while (true)
                {
                    AttributeRead read = ReadAttribute(bytes, ref position, out string name, out string value);
                    if (read == AttributeRead.EndOfBytes)
                        return XmlEncoding(bytes);

                    if (read == AttributeRead.TagEnd)
                    {
                        position++;
                        break;
                    }

                    if (!seen.Add(name))
                        continue;

                    switch (name)
                    {
                        case "http-equiv" when value == "content-type":
                            gotPragma = true;
                            break;

                        case "content" when !charsetSet:
                            if (ExtractFromContent(value) is { } fromContent)
                            {
                                charset = fromContent;
                                charsetSet = true;
                                needPragma = true;
                            }

                            break;

                        case "charset":
                            charset = EncodingForLabel(value);
                            charsetSet = true;
                            needPragma = false;
                            break;
                    }
                }

                if (needPragma is not null && (!needPragma.Value || gotPragma) && charset is not null)
                    return charset;

                continue;
            }

            if (IsTagStart(bytes, position))
            {
                if (!SkipTag(bytes, ref position))
                    return XmlEncoding(bytes);

                continue;
            }

            if (StartsWith(bytes, position, "<!"u8) ||
                StartsWith(bytes, position, "</"u8) ||
                StartsWith(bytes, position, "<?"u8))
            {
                int end = bytes[position..].IndexOf((byte)'>');
                if (end < 0)
                    return XmlEncoding(bytes);

                position += end + 1;
                continue;
            }

            position++;
        }

        return XmlEncoding(bytes);
    }

    private static bool SkipTag(ReadOnlySpan<byte> bytes, ref int position)
    {
        position++;
        if (position < bytes.Length && bytes[position] == (byte)'/')
            position++;

        while (position < bytes.Length && !IsSpace(bytes[position]) && bytes[position] != (byte)'>')
            position++;

        if (position >= bytes.Length)
            return false;

        if (bytes[position] == (byte)'>')
        {
            position++;
            return true;
        }

        while (true)
        {
            AttributeRead read = ReadAttribute(bytes, ref position, out _, out _);
            if (read == AttributeRead.EndOfBytes)
                return false;

            if (read != AttributeRead.TagEnd)
                continue;

            position++;
            return true;
        }
    }

    private static AttributeRead ReadAttribute(
        ReadOnlySpan<byte> bytes,
        ref int position,
        out string name,
        out string value)
    {
        name = string.Empty;
        value = string.Empty;

        while (position < bytes.Length && (IsSpace(bytes[position]) || bytes[position] == (byte)'/'))
            position++;

        if (position >= bytes.Length)
            return AttributeRead.EndOfBytes;

        if (bytes[position] == (byte)'>')
            return AttributeRead.TagEnd;

        var attributeName = new StringBuilder();
        while (position < bytes.Length)
        {
            byte current = bytes[position];
            if (current == (byte)'=' && attributeName.Length > 0)
            {
                position++;
                return ReadAttributeValue(bytes, ref position, attributeName, out name, out value);
            }

            if (IsSpace(current))
                break;

            if (current is (byte)'/' or (byte)'>')
            {
                name = attributeName.ToString();
                return AttributeRead.Attribute;
            }

            attributeName.Append(AsciiLower(current));
            position++;
        }

        if (position >= bytes.Length)
            return AttributeRead.EndOfBytes;

        while (position < bytes.Length && IsSpace(bytes[position]))
            position++;

        if (position >= bytes.Length)
            return AttributeRead.EndOfBytes;

        if (bytes[position] != (byte)'=')
        {
            name = attributeName.ToString();
            return AttributeRead.Attribute;
        }

        position++;
        return ReadAttributeValue(bytes, ref position, attributeName, out name, out value);
    }

    private static AttributeRead ReadAttributeValue(
        ReadOnlySpan<byte> bytes,
        ref int position,
        StringBuilder attributeName,
        out string name,
        out string value)
    {
        name = string.Empty;
        value = string.Empty;

        while (position < bytes.Length && IsSpace(bytes[position]))
            position++;

        if (position >= bytes.Length)
            return AttributeRead.EndOfBytes;

        var attributeValue = new StringBuilder();
        byte current = bytes[position];
        if (current is (byte)'\"' or (byte)'\'')
        {
            byte quote = current;
            position++;
            while (position < bytes.Length && bytes[position] != quote)
            {
                attributeValue.Append(AsciiLower(bytes[position]));
                position++;
            }

            if (position >= bytes.Length)
                return AttributeRead.EndOfBytes;

            position++;
        }
        else if (current != (byte)'>')
        {
            while (position < bytes.Length && !IsSpace(bytes[position]) && bytes[position] != (byte)'>')
            {
                attributeValue.Append(AsciiLower(bytes[position]));
                position++;
            }

            if (position >= bytes.Length)
                return AttributeRead.EndOfBytes;
        }

        name = attributeName.ToString();
        value = attributeValue.ToString();
        return AttributeRead.Attribute;
    }

    private static Encoding? ExtractFromContent(string content)
    {
        int searchFrom = 0;
        while (searchFrom <= content.Length - 7)
        {
            int found = content.IndexOf("charset", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return null;

            int position = found + 7;
            while (position < content.Length && IsSpace(content[position]))
                position++;

            if (position >= content.Length || content[position] != '=')
            {
                searchFrom = position;
                continue;
            }

            position++;
            while (position < content.Length && IsSpace(content[position]))
                position++;

            if (position >= content.Length)
                return null;

            char first = content[position];
            if (first is '\"' or '\'')
            {
                int end = content.IndexOf(first, position + 1);
                return end < 0 ? null : EncodingForLabel(content[(position + 1)..end]);
            }

            int unquotedEnd = position;
            while (unquotedEnd < content.Length &&
                   !IsSpace(content[unquotedEnd]) && content[unquotedEnd] != ';')
            {
                unquotedEnd++;
            }

            return EncodingForLabel(content[position..unquotedEnd]);
        }

        return null;
    }

    private static Encoding? XmlEncoding(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith("<?xml"u8))
            return null;

        int declarationEnd = bytes.IndexOf((byte)'>');
        if (declarationEnd < 0)
            return null;

        int encodingAt = IndexOf(bytes[..declarationEnd], 0, "encoding"u8);
        if (encodingAt < 0)
            return null;

        int position = encodingAt + 8;
        while (position < declarationEnd && bytes[position] <= 0x20)
            position++;

        if (position >= declarationEnd || bytes[position] != (byte)'=')
            return null;

        position++;
        while (position < declarationEnd && bytes[position] <= 0x20)
            position++;

        if (position >= declarationEnd || bytes[position] is not ((byte)'\"' or (byte)'\''))
            return null;

        byte quote = bytes[position++];
        int end = bytes[position..declarationEnd].IndexOf(quote);
        if (end < 0)
            return null;

        ReadOnlySpan<byte> labelBytes = bytes.Slice(position, end);
        foreach (byte character in labelBytes)
        {
            if (character <= 0x20)
                return null;
        }

        return EncodingForLabel(Encoding.Latin1.GetString(labelBytes));
    }

    private static Encoding? EncodingForLabel(string label)
    {
        string normalized = AsciiLower(TrimAsciiWhitespace(label));
        switch (normalized)
        {
            case "unicode-1-1-utf-8" or "unicode11utf8" or "unicode20utf8" or "utf-8" or "utf8" or
                 "x-unicode20utf8":
                return Encoding.UTF8;

            // An in-document UTF-16 declaration is explicitly interpreted as UTF-8.
            case "csunicode" or "iso-10646-ucs-2" or "ucs-2" or "unicode" or "unicodefeff" or
                 "unicodefffe" or "utf-16" or "utf-16be" or "utf-16le":
                return Encoding.UTF8;

            // HTML's prescan maps x-user-defined to windows-1252.
            case "x-user-defined":
                return CodePage(1252);

            // Encoding Standard labels that .NET otherwise decodes as ASCII or ISO-8859-1.
            case "ansi_x3.4-1968" or "ascii" or "cp1252" or "cp819" or "csisolatin1" or "ibm819" or
                 "iso-8859-1" or "iso-ir-100" or "iso8859-1" or "iso88591" or "iso_8859-1" or
                 "iso_8859-1:1987" or "l1" or "latin1" or "us-ascii" or "windows-1252" or "x-cp1252":
                return CodePage(1252);

            case "iso-8859-9" or "iso-ir-148" or "iso8859-9" or "iso88599" or "iso_8859-9" or
                 "iso_8859-9:1989" or "l5" or "latin5":
                return CodePage(1254);

            case "iso-8859-11" or "tis-620":
                return CodePage(874);

            case "iso-8859-8-i":
                return CodePage(28598);

            case "gb_2312-80" or "gb2312" or "gb2312-80" or "iso-ir-58" or "x-gbk":
                return CodePage(936);

            case "ks_c_5601-1987" or "ks_c_5601-1989" or "ksc5601" or "korean" or "iso-ir-149":
                return CodePage(51949);

            // These are deliberately prohibited by the HTML standard even when .NET knows them.
            case "utf-7" or "unicode-1-1-utf-7" or "csunicode11utf7" or "x-unicode20utf7" or
                 "hz-gb-2312" or "csiso2022kr" or "iso-2022-kr" or "iso-2022-cn" or "iso-2022-cn-ext":
                return null;
        }

        Encoding? candidate;
        try
        {
            candidate = CodePagesEncodingProvider.Instance.GetEncoding(normalized);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return candidate is not null && IsHtmlEncoding(candidate.CodePage) ? candidate : null;
    }

    private static bool IsHtmlEncoding(int codePage) => codePage is
        866 or
        874 or
        932 or
        936 or
        950 or
        1250 or 1251 or 1252 or 1253 or 1254 or 1255 or 1256 or 1257 or 1258 or
        10000 or 10007 or
        20866 or 21866 or
        28592 or 28593 or 28594 or 28595 or 28596 or 28597 or 28598 or
        28600 or 28603 or 28604 or 28605 or 28606 or
        50220 or 51932 or 51949 or 54936 or
        65001;

    private static Encoding CodePage(int codePage) =>
        CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.UTF8;

    private static bool IsMetaStart(ReadOnlySpan<byte> bytes, int position) =>
        position + 5 < bytes.Length &&
        bytes[position] == (byte)'<' &&
        AsciiLower(bytes[position + 1]) == 'm' &&
        AsciiLower(bytes[position + 2]) == 'e' &&
        AsciiLower(bytes[position + 3]) == 't' &&
        AsciiLower(bytes[position + 4]) == 'a' &&
        (IsSpace(bytes[position + 5]) || bytes[position + 5] == (byte)'/');

    private static bool IsTagStart(ReadOnlySpan<byte> bytes, int position)
    {
        if (position >= bytes.Length || bytes[position] != (byte)'<')
            return false;

        position++;
        if (position < bytes.Length && bytes[position] == (byte)'/')
            position++;

        return position < bytes.Length && IsAsciiLetter(bytes[position]);
    }

    private static bool StartsWith(ReadOnlySpan<byte> bytes, int position, ReadOnlySpan<byte> value) =>
        position >= 0 && position <= bytes.Length - value.Length && bytes[position..].StartsWith(value);

    private static int IndexOf(ReadOnlySpan<byte> bytes, int start, ReadOnlySpan<byte> value)
    {
        if (start < 0 || start > bytes.Length)
            return -1;

        int relative = bytes[start..].IndexOf(value);
        return relative < 0 ? -1 : start + relative;
    }

    private static string TrimAsciiWhitespace(string value)
    {
        int start = 0;
        while (start < value.Length && IsSpace(value[start]))
            start++;

        int end = value.Length;
        while (end > start && IsSpace(value[end - 1]))
            end--;

        return value[start..end];
    }

    private static string AsciiLower(string value)
    {
        char[]? folded = null;
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character is not (>= 'A' and <= 'Z'))
                continue;

            folded ??= value.ToCharArray();
            folded[i] = (char)(character + 0x20);
        }

        return folded is null ? value : new string(folded);
    }

    private static char AsciiLower(byte value) =>
        (char)(value is >= (byte)'A' and <= (byte)'Z' ? value + 0x20 : value);

    private static bool IsAsciiLetter(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';

    private static bool IsSpace(byte value) => value is 0x09 or 0x0A or 0x0C or 0x0D or 0x20;

    private static bool IsSpace(char value) => value is '\t' or '\n' or '\f' or '\r' or ' ';
}
