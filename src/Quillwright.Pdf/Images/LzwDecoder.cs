namespace Quillwright.Pdf.Images;

/// <summary>
/// The LZW variant both GIF and TIFF use, which is one algorithm written down twice with two
/// differences: which end of a byte the codes are packed from, and whether the code width grows
/// one code before the dictionary is actually full.
/// </summary>
internal sealed class LzwDecoder
{
    private const int MaxCodeBits = 12;
    private const int MaxCodes = 1 << MaxCodeBits;

    private readonly short[] _prefix = new short[MaxCodes];
    private readonly byte[] _suffix = new byte[MaxCodes];
    private readonly byte[] _stack = new byte[MaxCodes];

    private readonly bool _msbFirst;
    private readonly int _early;
    private readonly int _clear;
    private readonly int _stop;
    private readonly int _minimumWidth;

    private LzwDecoder(int minCodeSize, bool msbFirst, bool earlyChange)
    {
        _msbFirst = msbFirst;
        _early = earlyChange ? 1 : 0;
        _clear = 1 << minCodeSize;
        _stop = _clear + 1;
        _minimumWidth = minCodeSize + 1;
    }

    /// <summary>Decodes a stream, stopping at its end marker or at the size the caller expects.</summary>
    /// <param name="data">The packed codes.</param>
    /// <param name="minCodeSize">Width of a literal, which fixes where the dictionary starts.</param>
    /// <param name="msbFirst">Whether codes are packed from the top of each byte, as TIFF does.</param>
    /// <param name="earlyChange">Whether the code width grows one code early, as TIFF does.</param>
    /// <param name="limit">How many bytes the caller has room for.</param>
    public static byte[]? Decode(ReadOnlySpan<byte> data, int minCodeSize, bool msbFirst, bool earlyChange, int limit)
    {
        if (minCodeSize is < 2 or > 11 || limit <= 0)
            return null;

        return new LzwDecoder(minCodeSize, msbFirst, earlyChange).Run(data, limit);
    }

    private byte[]? Run(ReadOnlySpan<byte> data, int limit)
    {
        byte[] output = new byte[limit];
        int written = 0;
        var bits = new BitCursor(data, _msbFirst);

        int next = _clear + 2;
        int width = _minimumWidth;
        int previous = -1;

        while (written < limit && bits.TryRead(width, out int code))
        {
            if (code == _stop)
                break;

            if (code == _clear)
            {
                next = _clear + 2;
                width = _minimumWidth;
                previous = -1;
                continue;
            }

            int emitted = Emit(code, previous, next, output, ref written);
            if (emitted < 0)
                break;

            if (previous >= 0 && next < MaxCodes)
            {
                _prefix[next] = (short)previous;
                _suffix[next] = (byte)emitted;
                next++;
            }

            if (next + _early >= 1 << width && width < MaxCodeBits)
                width++;

            previous = code;
        }

        return written == 0 ? null : Trim(output, written, limit);
    }

    /// <summary>
    /// Writes out what a code stands for and gives back the first byte of it, which is what the
    /// dictionary entry for the code before this one ends with.
    /// </summary>
    /// <returns>The first byte of the sequence, or a negative number when the code is unusable.</returns>
    private int Emit(int code, int previous, int next, byte[] output, ref int written)
    {
        int depth = 0;
        int current = code;

        // The one self-referential case the format allows: a code for a sequence that is only
        // being defined by its own use, which always ends with the byte it starts with.
        if (code >= next)
        {
            if (previous < 0 || code > next)
                return -1;

            current = previous;
            _stack[depth++] = FirstByte(previous);
        }

        while (current >= _clear + 2 && depth < MaxCodes)
        {
            _stack[depth++] = _suffix[current];
            current = _prefix[current];
        }

        if (current >= _clear)
            return -1;

        _stack[depth++] = (byte)current;
        for (int i = depth - 1; i >= 0 && written < output.Length; i--)
            output[written++] = _stack[i];

        return _stack[depth - 1];
    }

    private byte FirstByte(int code)
    {
        int current = code;
        while (current >= _clear + 2)
            current = _prefix[current];

        return (byte)Math.Min(current, _clear - 1);
    }

    private static byte[] Trim(byte[] output, int written, int limit) =>
        written == limit ? output : output[..written];

    /// <summary>Reads codes of a given width out of a byte stream, from either end of a byte.</summary>
    private ref struct BitCursor(ReadOnlySpan<byte> data, bool msbFirst)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private readonly bool _msbFirst = msbFirst;
        private int _at;
        private int _accumulator;
        private int _bits;

        /// <summary>Reads the next code, or reports that the stream ran out.</summary>
        /// <param name="width">How many bits the code takes.</param>
        /// <param name="code">The code that was read.</param>
        public bool TryRead(int width, out int code)
        {
            while (_bits < width)
            {
                if (_at >= _data.Length)
                {
                    code = 0;
                    return false;
                }

                _accumulator = _msbFirst ? (_accumulator << 8) | _data[_at] : _accumulator | (_data[_at] << _bits);
                _bits += 8;
                _at++;
            }

            int mask = (1 << width) - 1;
            if (_msbFirst)
            {
                code = (_accumulator >> (_bits - width)) & mask;
                _bits -= width;

                // The bits just consumed stay in the accumulator otherwise, and every byte read
                // after them shifts the leftovers further up until the whole thing overflows.
                _accumulator &= (1 << _bits) - 1;
            }
            else
            {
                code = _accumulator & mask;
                _accumulator >>= width;
                _bits -= width;
            }

            return true;
        }
    }
}
