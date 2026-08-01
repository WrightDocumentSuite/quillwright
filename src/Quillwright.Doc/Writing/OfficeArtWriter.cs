using System.Buffers.Binary;
using System.Security.Cryptography;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Writes an inline picture as the drawing container the binary format keeps in its data
/// stream: a header describing the frame, a shape, and the image bytes themselves.
/// </summary>
/// <remarks>
/// The drawing layer is a separate format ([MS-ODRAW]) that Word embeds whole. Only the
/// smallest arrangement that displays one inline bitmap is written here — a picture frame
/// shape whose single property points at one blip — because anything richer would mean
/// modelling a drawing canvas the document model does not have.
/// </remarks>
internal static class OfficeArtWriter
{
    private const int HeaderBytes = 68;
    private const int PictureFrameShape = 75;

    /// <summary>Returns <see langword="true"/> for image formats the drawing layer can hold directly.</summary>
    public static bool IsSupported(ImageData image) =>
        image.ContentType is "image/png" or "image/jpeg";

    /// <summary>Builds the whole record for one picture, ready to append to the data stream.</summary>
    public static byte[] Build(Picture picture)
    {
        byte[] drawing = Drawing(picture.Image);
        var bytes = new List<byte>(HeaderBytes + drawing.Length);

        Length width = picture.Width != Length.Zero ? picture.Width : picture.Image.NaturalWidth;
        Length height = picture.Height != Length.Zero ? picture.Height : picture.Image.NaturalHeight;

        Span<byte> header = stackalloc byte[HeaderBytes];
        header.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(header, HeaderBytes + drawing.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], HeaderBytes);
        BinaryPrimitives.WriteInt16LittleEndian(header[6..], 0x0064);  // The picture is a shape.

        // The goal size is what the picture is drawn at; the scale is a thousandth of a
        // percent, so a hundred percent is a hundred thousand.
        Span<byte> middle = header[28..];
        BinaryPrimitives.WriteInt16LittleEndian(middle, Twips(width));
        BinaryPrimitives.WriteInt16LittleEndian(middle[2..], Twips(height));
        BinaryPrimitives.WriteInt16LittleEndian(middle[4..], 1000);
        BinaryPrimitives.WriteInt16LittleEndian(middle[6..], 1000);

        bytes.AddRange(header);
        bytes.AddRange(drawing);
        return [.. bytes];
    }

    private static short Twips(Length value) => (short)Math.Clamp(value.Twips, 1, short.MaxValue);

    /// <summary>The shape container followed by the image bytes it refers to.</summary>
    private static byte[] Drawing(ImageData image)
    {
        byte[] shape = Record(version: 2, instance: PictureFrameShape, type: 0xF00A, ShapeBody());
        byte[] options = Record(version: 3, instance: 1, type: 0xF00B, PictureProperty());
        byte[] container = Record(version: 0xF, instance: 0, type: 0xF004, [.. shape, .. options]);
        return [.. container, .. Blip(image)];
    }

    private static byte[] ShapeBody()
    {
        var body = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0x00000A00); // Has an anchor and a shape type.
        return body;
    }

    /// <summary>The one shape property that matters: which stored image this frame shows.</summary>
    private static byte[] PictureProperty()
    {
        var property = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(property, 0x4104);
        BinaryPrimitives.WriteUInt32LittleEndian(property.AsSpan(2), 1);
        return property;
    }

    private static byte[] Blip(ImageData image)
    {
        bool png = image.ContentType == "image/png";
        byte[] content = image.Bytes.ToArray();
        byte[] identity = MD5.HashData(content);

        var body = new byte[identity.Length + 1 + content.Length];
        identity.CopyTo(body, 0);
        body[identity.Length] = 0xFF;
        content.CopyTo(body, identity.Length + 1);

        return Record(
            version: 0,
            instance: png ? 0x6E0 : 0x46A,
            type: png ? (ushort)0xF01E : (ushort)0xF01D,
            body);
    }

    /// <summary>Wraps a body in the eight-byte header every drawing record starts with.</summary>
    private static byte[] Record(int version, int instance, ushort type, byte[] body)
    {
        var record = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(record, (ushort)((version & 0xF) | (instance << 4)));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(2), type);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), body.Length);
        body.CopyTo(record, 8);
        return record;
    }
}
