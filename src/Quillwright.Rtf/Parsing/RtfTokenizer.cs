namespace Quillwright.Rtf.Parsing;

internal ref struct RtfTokenizer
{
    private const int MaximumControlWordLength = 32;
    private const int MaximumParameterDigits = 10;

    private readonly ReadOnlySpan<byte> _input;
    private int _offset;
    private int _pendingBinaryLength;

    public RtfTokenizer(ReadOnlySpan<byte> input)
    {
        _input = input;
        _offset = 0;
        _pendingBinaryLength = -1;
    }

    public ReadOnlySpan<byte> Slice(RtfToken token) => _input.Slice(token.Offset, token.Length);

    public bool TryRead(out RtfToken token)
    {
        if (_pendingBinaryLength >= 0)
        {
            int binaryStart = _offset;
            if (_pendingBinaryLength > _input.Length - _offset)
                throw new RtfFormatException("The binary payload is shorter than its \\bin length", binaryStart);

            _offset += _pendingBinaryLength;
            token = new RtfToken(RtfTokenKind.Binary, binaryStart, _pendingBinaryLength);
            _pendingBinaryLength = -1;
            return true;
        }

        while (_offset < _input.Length && _input[_offset] is (byte)'\r' or (byte)'\n')
            _offset++;

        if (_offset >= _input.Length)
        {
            token = default;
            return false;
        }

        int start = _offset;
        byte current = _input[_offset++];
        switch (current)
        {
            case (byte)'{':
                token = new RtfToken(RtfTokenKind.GroupStart, start);
                return true;
            case (byte)'}':
                token = new RtfToken(RtfTokenKind.GroupEnd, start);
                return true;
            case (byte)'\\':
                return ReadControl(start, out token);
            default:
                while (_offset < _input.Length && _input[_offset] is not ((byte)'{' or (byte)'}' or (byte)'\\' or (byte)'\r' or (byte)'\n'))
                    _offset++;
                token = new RtfToken(RtfTokenKind.Text, start, _offset - start);
                return true;
        }
    }

    private bool ReadControl(int start, out RtfToken token)
    {
        if (_offset >= _input.Length)
            throw new RtfFormatException("A trailing backslash does not form an RTF control", start);

        byte first = _input[_offset];
        if (!IsAsciiLetter(first))
            return ReadControlSymbol(start, out token);

        int nameStart = _offset++;
        while (_offset < _input.Length && IsAsciiLetter(_input[_offset]))
            _offset++;

        int nameLength = _offset - nameStart;
        if (nameLength > MaximumControlWordLength)
            throw new RtfFormatException("An RTF control word is longer than 32 letters", nameStart);

        bool negative = _offset < _input.Length && _input[_offset] == (byte)'-';
        if (negative)
            _offset++;

        int digitStart = _offset;
        long parameter = 0;
        while (_offset < _input.Length && IsAsciiDigit(_input[_offset]))
        {
            if (_offset - digitStart >= MaximumParameterDigits)
                throw new RtfFormatException("An RTF numeric parameter is longer than 10 digits", digitStart);
            parameter = (parameter * 10) + (_input[_offset++] - (byte)'0');
        }

        int digitCount = _offset - digitStart;
        if (negative && digitCount == 0)
            throw new RtfFormatException("A minus sign in an RTF parameter is not followed by digits", digitStart);

        bool hasParameter = digitCount > 0;
        if (negative)
            parameter = -parameter;
        if (parameter is < int.MinValue or > int.MaxValue)
            throw new RtfFormatException("An RTF numeric parameter is outside the 32-bit range", digitStart);

        if (_offset < _input.Length && _input[_offset] == (byte)' ')
            _offset++;

        RtfKeyword keyword = RtfKeywordLookup.Find(_input.Slice(nameStart, nameLength));
        int value = (int)parameter;
        if (keyword == RtfKeyword.Binary)
        {
            if (!hasParameter || value < 0)
                throw new RtfFormatException("The \\bin control requires a non-negative length", start);
            _pendingBinaryLength = value;
        }

        token = new RtfToken(RtfTokenKind.ControlWord, start, nameLength, keyword, value, hasParameter);
        return true;
    }

    private bool ReadControlSymbol(int start, out RtfToken token)
    {
        byte symbol = _input[_offset++];
        if (symbol == (byte)'\'')
        {
            if (_input.Length - _offset < 2 || !TryHex(_input[_offset], out int high) || !TryHex(_input[_offset + 1], out int low))
                throw new RtfFormatException("The \\' control symbol is not followed by two hexadecimal digits", start);

            _offset += 2;
            token = new RtfToken(RtfTokenKind.HexByte, start, Parameter: (high << 4) | low);
            return true;
        }

        if (symbol is (byte)'\r' or (byte)'\n')
        {
            if (symbol == (byte)'\r' && _offset < _input.Length && _input[_offset] == (byte)'\n')
                _offset++;
            token = new RtfToken(RtfTokenKind.ControlWord, start, Keyword: RtfKeyword.Paragraph);
            return true;
        }

        token = new RtfToken(RtfTokenKind.ControlSymbol, start, Symbol: symbol);
        return true;
    }

    private static bool IsAsciiLetter(byte value) =>
        value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z';

    private static bool IsAsciiDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static bool TryHex(byte value, out int result)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            result = value - (byte)'0';
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            result = value - (byte)'a' + 10;
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            result = value - (byte)'A' + 10;
            return true;
        }

        result = 0;
        return false;
    }
}
