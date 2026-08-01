using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Writes pictures into the data stream and anchors them in the text.
/// </summary>
/// <remarks>
/// A picture is not stored in the text stream. The text holds one reserved character whose
/// character properties carry an offset into the data stream, and everything about the
/// picture — its size, its border, its bytes — lives at that offset.
/// </remarks>
internal sealed class PictureBuilder
{
    private readonly DocWriteContext _context;
    private readonly List<byte> _data = [];
    private readonly Dictionary<ImageData, int> _offsets = [];

    public PictureBuilder(DocWriteContext context) => _context = context;

    /// <summary>Returns <see langword="true"/> when no picture was written.</summary>
    public bool IsEmpty => _data.Count == 0;

    /// <summary>The data stream, which holds everything the text stream points at.</summary>
    public byte[] ToArray() => [.. _data];

    /// <summary>Anchors a picture in the text and stores its content in the data stream.</summary>
    public void Write(StoryAssembler story, Picture picture, RunFormat format)
    {
        if (!_context.WriteImages)
            return;

        if (!OfficeArtWriter.IsSupported(picture.Image))
        {
            _context.Warn(
                WarningCode.UnresolvedMedia,
                $"A {picture.Image.ContentType} image cannot be written to the binary format and was dropped.");
            return;
        }

        if (!_offsets.TryGetValue(picture.Image, out int offset))
        {
            offset = _data.Count;
            _data.AddRange(OfficeArtWriter.Build(picture));
            _offsets[picture.Image] = offset;
        }

        story.WriteSpecial(
            DocChar.Picture,
            _context.BuildSpecialRun(format, writer => writer.Int32(SprmCode.PictureLocation, offset)));
    }

    /// <summary>Reserves space in the data stream for property lists too large to sit in a page.</summary>
    /// <param name="properties">The oversized property list.</param>
    /// <returns>The offset the paragraph should point at.</returns>
    public int StoreProperties(byte[] properties)
    {
        int offset = _data.Count;
        _data.Add((byte)properties.Length);
        _data.Add((byte)(properties.Length >> 8));
        _data.AddRange(properties);
        if (_data.Count % 2 != 0)
            _data.Add(0);
        return offset;
    }
}
