using System.Buffers.Binary;

namespace Quillwright.Pdf.Images;

/// <summary>Somewhere for a decoder to put pixels, in the order a PDF image wants them.</summary>
internal sealed class Canvas
{
    private readonly byte[] _samples;
    private readonly byte[]? _alpha;
    private readonly int _width;
    private readonly int _height;

    /// <summary>Makes room for an image of a given size.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="alpha">Whether any pixel can be less than opaque.</param>
    public Canvas(int width, int height, bool alpha)
    {
        _width = width;
        _height = height;
        _samples = new byte[width * height * 3];
        _alpha = alpha ? new byte[width * height] : null;
    }

    /// <summary>Paints one pixel, ignoring a position outside the image.</summary>
    /// <param name="x">Distance from the left edge.</param>
    /// <param name="y">Distance from the top edge.</param>
    /// <param name="colour">Red, green and blue in the low three bytes.</param>
    /// <param name="alpha">How opaque the pixel is.</param>
    public void Set(int x, int y, uint colour, byte alpha)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
            return;

        int at = ((y * _width) + x) * 3;
        _samples[at] = (byte)(colour >> 16);
        _samples[at + 1] = (byte)(colour >> 8);
        _samples[at + 2] = (byte)colour;

        if (_alpha is not null)
            _alpha[(y * _width) + x] = alpha;
    }

    /// <summary>
    /// The finished image. A mask that turned out to say nothing — every pixel opaque, or every
    /// pixel clear, which is what an undeclared alpha channel full of zeroes looks like — is
    /// dropped rather than carried into the PDF.
    /// </summary>
    public RasterImage ToImage()
    {
        byte[]? mask = _alpha;
        if (mask is not null && (Array.TrueForAll(mask, static value => value == 0xFF) ||
                                 Array.TrueForAll(mask, static value => value == 0)))
        {
            mask = null;
        }

        return new RasterImage(_width, _height, _samples, 3, mask);
    }
}

/// <summary>
/// Where each channel of a packed pixel lives. A bitmap either states the masks or is one of the
/// two arrangements the format defaults to ([MS-WMF] 2.1.1.4).
/// </summary>
/// <param name="Red">Bits of the red channel.</param>
/// <param name="Green">Bits of the green channel.</param>
/// <param name="Blue">Bits of the blue channel.</param>
/// <param name="AlphaMask">Bits of the alpha channel, or zero when the image is opaque.</param>
internal readonly record struct ChannelMasks(uint Red, uint Green, uint Blue, uint AlphaMask)
{
    private const uint Default16Red = 0x7C00;
    private const uint Default16Green = 0x03E0;
    private const uint Default16Blue = 0x001F;

    private const uint Default32Red = 0x00FF0000;
    private const uint Default32Green = 0x0000FF00;
    private const uint Default32Blue = 0x000000FF;

    /// <summary>Packs a pixel's colour channels into the low three bytes of an integer.</summary>
    /// <param name="value">The pixel as the file stores it.</param>
    public uint Apply(uint value) =>
        ((uint)Channel(value, Red) << 16) | ((uint)Channel(value, Green) << 8) | Channel(value, Blue);

    /// <summary>How opaque a pixel is, which is fully so when no alpha channel was declared.</summary>
    /// <param name="value">The pixel as the file stores it.</param>
    public byte Alpha(uint value) => AlphaMask == 0 ? (byte)0xFF : Channel(value, AlphaMask);

    /// <summary>
    /// Reads the masks a bitmap declares, or the defaults for its depth. The three that follow a
    /// plain header are read from after it; a longer header states them, and an alpha mask at
    /// all, inside itself.
    /// </summary>
    /// <param name="dib">The bitmap, starting at its header.</param>
    /// <param name="headerSize">Length of the header, which says which shape it is.</param>
    /// <param name="bits">How many bits a pixel takes.</param>
    /// <param name="compression">What the header says about how the pixels are stored.</param>
    public static ChannelMasks For(ReadOnlySpan<byte> dib, int headerSize, int bits, uint compression)
    {
        const uint BitFields = 3;
        const uint AlphaBitFields = 6;

        ChannelMasks defaults = bits == 16
            ? new ChannelMasks(Default16Red, Default16Green, Default16Blue, 0)
            : new ChannelMasks(Default32Red, Default32Green, Default32Blue, 0);

        if (bits is not (16 or 32))
            return defaults;

        // A header longer than the plain one carries the masks; a plain one is followed by them
        // only when it says the pixels are stored channel by channel.
        int at = headerSize > 40 ? 40 : (compression is BitFields or AlphaBitFields ? headerSize : -1);
        if (at < 0 || at + 12 > dib.Length)
            return defaults;

        uint red = BinaryPrimitives.ReadUInt32LittleEndian(dib[at..]);
        uint green = BinaryPrimitives.ReadUInt32LittleEndian(dib[(at + 4)..]);
        uint blue = BinaryPrimitives.ReadUInt32LittleEndian(dib[(at + 8)..]);
        if ((red | green | blue) == 0)
            return defaults;

        bool hasAlpha = headerSize >= 56 || compression == AlphaBitFields;
        uint alpha = hasAlpha && at + 16 <= dib.Length ? BinaryPrimitives.ReadUInt32LittleEndian(dib[(at + 12)..]) : 0;
        return new ChannelMasks(red, green, blue, alpha);
    }

    /// <summary>Pulls one channel out and stretches it to a full byte.</summary>
    private static byte Channel(uint value, uint mask)
    {
        if (mask == 0)
            return 0;

        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        uint width = mask >> shift;
        uint raw = (value & mask) >> shift;
        return width == 0 ? (byte)0 : (byte)(raw * 255 / width);
    }
}
