using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
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
/// shape whose properties point at one blip and carry its name and alternative text — because
/// anything richer would mean modelling a drawing canvas the document model does not have.
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
        byte[] drawing = Drawing(picture);
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
    private static byte[] Drawing(Picture picture)
    {
        (int count, byte[] properties) = Properties(picture);
        byte[] shape = Record(version: 2, instance: PictureFrameShape, type: 0xF00A, ShapeBody());
        byte[] options = Record(version: 3, instance: count, type: 0xF00B, properties);
        byte[] container = Record(version: 0xF, instance: 0, type: 0xF004, [.. shape, .. options]);
        return [.. container, .. Blip(picture.Image)];
    }

    private static byte[] ShapeBody()
    {
        var body = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0x00000A00); // Has an anchor and a shape type.
        return body;
    }

    /// <summary>
    /// The shape's property table: which stored image this frame shows, then its name and
    /// alternative text ([MS-ODRAW] 2.3.4.1 and 2.3.4.2). A string does not fit in its entry,
    /// so the entry holds its length and the UTF-16 text, nul included, follows the table.
    /// </summary>
    private static (int Count, byte[] Body) Properties(Picture picture)
    {
        var table = new List<byte>();
        var values = new List<byte>();

        Add(0x4104, 1);
        AddText(0x0380, picture.Name);
        AddText(0x0381, picture.Description);
        return (table.Count / 6, [.. table, .. values]);

        void Add(int identifier, int value)
        {
            var property = new byte[6];
            BinaryPrimitives.WriteUInt16LittleEndian(property, (ushort)identifier);
            BinaryPrimitives.WriteInt32LittleEndian(property.AsSpan(2), value);
            table.AddRange(property);
        }

        void AddText(int identifier, string? text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
            Add(identifier | 0x8000, bytes.Length);
            values.AddRange(bytes);
        }
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
