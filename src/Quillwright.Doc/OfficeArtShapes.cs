using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>
/// How a drawing is placed against the page element it belongs to ([MS-ODRAW] 2.3.4.19 and
/// 2.3.4.21). A shape is either offset by a distance or lined up with an edge, and which of
/// the two decides whether the rectangle in the text's anchor means anything.
/// </summary>
/// <param name="Horizontal">The <c>posh</c> property, or <c>-1</c> when the shape does not say.</param>
/// <param name="RelativeToHorizontal">The <c>posrelh</c> property, or <c>-1</c>.</param>
/// <param name="Vertical">The <c>posv</c> property, or <c>-1</c>.</param>
/// <param name="RelativeToVertical">The <c>posrelv</c> property, or <c>-1</c>.</param>
internal readonly record struct ShapePosition(int Horizontal, int RelativeToHorizontal, int Vertical, int RelativeToVertical)
{
    /// <summary>A shape that says nothing about its position.</summary>
    public static ShapePosition Unstated => new(-1, -1, -1, -1);
}

/// <summary>What one drawing says about itself, so far as this reader looks at it.</summary>
/// <param name="ShapeId">Identifier the text's anchor names the shape by.</param>
/// <param name="ShapeType">Which preset shape it is ([MS-ODRAW] 2.4.24), or zero for a group.</param>
/// <param name="ImageIndex">One-based place of the image in the document's store, or zero for none.</param>
/// <param name="ImageOffset">Offset of an image the shape carries itself, or <c>-1</c> for none.</param>
/// <param name="IsGroup">Whether the shape is a group of other shapes rather than one of its own.</param>
/// <param name="HasText">Whether the shape holds text.</param>
/// <param name="Position">How the shape is placed against the page.</param>
/// <param name="Appearance">What it is painted in.</param>
internal readonly record struct OfficeArtShape(
    int ShapeId,
    int ShapeType,
    int ImageIndex,
    int ImageOffset,
    bool IsGroup,
    bool HasText,
    ShapePosition Position,
    OfficeArtAppearance Appearance)
{
    /// <summary>Whether the shape displays a picture at all.</summary>
    public bool IsPicture => ImageIndex > 0 || ImageOffset >= 0;

    /// <summary>
    /// The name the specification gives this shape, for saying in a warning what was left
    /// behind. Only the kinds the reference corpus actually holds are named; anything else is
    /// reported by its number, which is more use than calling it a shape.
    /// </summary>
    public string TypeName => ShapeType switch
    {
        0 => "a group",
        1 => "a rectangle",
        2 => "a rounded rectangle",
        3 => "an ellipse",
        20 => "a line",
        75 => "a picture frame",
        202 => "a text box",
        >= 136 and <= 202 => "lettering",
        _ => "shape type " + ShapeType.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// The drawings of a document, indexed by the identifier the text refers to them by
/// ([MS-ODRAW] 2.2.14, <c>OfficeArtSpContainer</c>, inside [MS-DOC] 2.9.171).
/// </summary>
/// <remarks>
/// The text stream marks a floating drawing with one character and the anchor list turns that
/// into an identifier; only here does the identifier become a shape. Of everything a shape
/// says — its geometry, its fill, its line, its effects — this reads the two things the
/// document model can hold: whether the shape shows a picture, and whether it holds text.
/// </remarks>
internal sealed class OfficeArtShapes
{
    private const ushort DrawingGroup = 0xF000;
    private const ushort Drawing = 0xF002;
    private const ushort ShapeContainer = 0xF004;
    private const ushort ShapeProperties = 0xF00A;
    private const ushort PrimaryOptions = 0xF00B;
    private const ushort ShapeText = 0xF00D;

    /// <summary>Identifier of the property naming which stored image to display ([MS-ODRAW] 2.3.23.5).</summary>
    private const int ImageProperty = 0x0104;

    /// <summary>Identifiers of the four properties that place a shape ([MS-ODRAW] 2.3.4.19 to 2.3.4.22).</summary>
    private const int HorizontalProperty = 0x038F;
    private const int RelativeHorizontalProperty = 0x0390;
    private const int VerticalProperty = 0x0391;
    private const int RelativeVerticalProperty = 0x0392;

    /// <summary>How a shape is painted ([MS-ODRAW] 2.3.7, 2.3.8 and 2.3.2.1).</summary>
    private const int RotationProperty = 0x0004;
    private const int FillColorProperty = 0x0181;
    private const int FillBooleansProperty = 0x01BF;
    private const int LineColorProperty = 0x01C0;
    private const int LineWidthProperty = 0x01CB;
    private const int LineBooleansProperty = 0x01FF;
    private const int GeometryTextProperty = 0x00C0;

    /// <summary>
    /// The bits that say a fill or a line is drawn at all, and the bits that say those bits
    /// mean anything. A colour stated with the fill turned off is a colour nobody sees.
    /// </summary>
    private const uint FillOn = 0x00000010;
    private const uint FillStated = 0x00100000;
    private const uint LineOn = 0x00000008;
    private const uint LineStated = 0x00080000;

    /// <summary>Rotation is a fixed-point number with sixteen bits of fraction ([MS-ODRAW] 2.2.44).</summary>
    private const double FixedPoint = 65536.0;

    /// <summary>Bytes of one entry in a property table ([MS-ODRAW] 2.2.7).</summary>
    private const int PropertyBytes = 6;

    private readonly Dictionary<int, OfficeArtShape> _byId;

    private OfficeArtShapes(Dictionary<int, OfficeArtShape> byId) => _byId = byId;

    /// <summary>A document that draws nothing.</summary>
    public static OfficeArtShapes Empty { get; } = new([]);

    /// <summary>How many drawings the document has.</summary>
    public int Count => _byId.Count;

    /// <summary>The shape an anchor names, or nothing when the document has no such shape.</summary>
    /// <param name="shapeId">Identifier from the anchor.</param>
    public OfficeArtShape? ById(int shapeId) =>
        _byId.TryGetValue(shapeId, out OfficeArtShape shape) ? shape : null;

    /// <summary>Reads the drawings of a document.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="region">Where the drawings live, and how long they are.</param>
    public static OfficeArtShapes Read(byte[] table, (int Offset, int Length) region)
    {
        (int offset, int length) = region;
        if (length <= 0 || offset < 0 || offset + length > table.Length)
            return Empty;

        int end = offset + length;
        if (OfficeArtRecord.Find(table, offset, end, DrawingGroup) is not { } group)
            return Empty;

        var shapes = new Dictionary<int, OfficeArtShape>();

        // What follows the document-wide records is one drawing per story, each headed by a
        // single byte saying which story it belongs to ([MS-DOC] 2.9.172).
        int position = group.End;
        while (OfficeArtRecord.TryRead(table, position + 1, end, out OfficeArtRecord drawing) && drawing.Type == Drawing)
        {
            Collect(table, drawing.Body, drawing.End, shapes);
            position = drawing.End;
        }

        return shapes.Count == 0 ? Empty : new OfficeArtShapes(shapes);
    }

    /// <summary>Gathers every shape between two offsets, descending into groups of them.</summary>
    private static void Collect(byte[] table, int start, int end, Dictionary<int, OfficeArtShape> shapes)
    {
        foreach (OfficeArtRecord record in OfficeArtRecord.Walk(table, start, end))
        {
            if (record.Type == ShapeContainer)
            {
                if (Read(table, record) is { } shape)
                    shapes[shape.ShapeId] = shape;
                continue;
            }

            if (record.IsContainer)
                Collect(table, record.Body, record.End, shapes);
        }
    }

    /// <summary>Reads the parts of one shape container this reader looks at.</summary>
    private static OfficeArtShape? Read(byte[] table, OfficeArtRecord container)
    {
        int shapeId = -1;
        int shapeType = 0;
        bool group = false;
        bool text = false;
        var found = new Options();

        foreach (OfficeArtRecord record in container.Children(table))
        {
            switch (record.Type)
            {
                case ShapeProperties when record.Length >= 8:
                    // The preset shape is the instance field of the record's own header.
                    shapeId = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(record.Body));
                    shapeType = record.Instance;
                    group = (BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(record.Body + 4)) & 0x1) != 0;
                    break;
                case PrimaryOptions:
                    ReadOptions(table, record, found);
                    break;
                case ShapeText:
                    text = true;
                    break;
            }
        }

        return shapeId >= 0
            ? new OfficeArtShape(
                shapeId, shapeType, found.Image.Index, found.Image.Offset, group, text, found.Position, found.Appearance)
            : null;
    }

    /// <summary>
    /// What a shape's property table says: which image it shows, and where it sits. A
    /// property is six bytes; one whose value does not fit in them stores its length instead,
    /// and the values themselves follow the table in the order their properties appear in it,
    /// so the whole table is walked even when only a few entries are wanted.
    /// </summary>
    private static void ReadOptions(byte[] table, OfficeArtRecord options, Options found)
    {
        int complex = options.Body + (options.Instance * PropertyBytes);
        for (int i = 0; i < options.Instance; i++)
        {
            int at = options.Body + (i * PropertyBytes);
            if (at + PropertyBytes > options.End)
                break;

            ushort identifier = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(at));
            int value = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(at + 2));
            bool stored = (identifier & 0x8000) != 0;

            found.Take(identifier & 0x3FFF, value, stored, table, complex);
            if (stored && value > 0)
                complex += value;
        }
    }

    /// <summary>Where a shape's picture is: a place in the document's store, or bytes of its own.</summary>
    private readonly record struct ImageReference(int Index, int Offset);

    /// <summary>
    /// What a shape's property table said, gathered as the table is walked.
    /// </summary>
    /// <remarks>
    /// A fill or a line is two properties: the colour, and a bit saying whether it is drawn.
    /// They can arrive in either order and either can be absent, so the colours are held until
    /// the whole table has been read and only then turned into what the shape shows.
    /// </remarks>
    private sealed class Options
    {
        private int? _fillColor;
        private int? _lineColor;
        private int? _lineWidth;
        private uint _fillBooleans;
        private uint _lineBooleans;
        private double _rotation;
        private string? _geometryText;

        public ImageReference Image { get; private set; } = new(0, -1);

        public ShapePosition Position { get; private set; } = ShapePosition.Unstated;

        /// <summary>What the shape shows, once every property has been seen.</summary>
        public OfficeArtAppearance Appearance => new(
            Drawn(_fillBooleans, FillOn, FillStated, defaultOn: true) ? OfficeArtAppearance.Color(_fillColor ?? 0) : null,
            Drawn(_lineBooleans, LineOn, LineStated, defaultOn: true) ? OfficeArtAppearance.Color(_lineColor ?? 0) : null,
            Drawn(_lineBooleans, LineOn, LineStated, defaultOn: true) && _lineWidth is { } width
                ? Primitives.Length.FromEmu(width)
                : null,
            _rotation,
            _geometryText);

        /// <summary>Records one property of the table.</summary>
        /// <param name="identifier">Which property it is.</param>
        /// <param name="value">Its value, or the length of one stored after the table.</param>
        /// <param name="stored">Whether the value is stored after the table rather than here.</param>
        /// <param name="table">The stream the table lives in.</param>
        /// <param name="complex">Where this property's stored value begins.</param>
        public void Take(int identifier, int value, bool stored, byte[] table, int complex)
        {
            switch (identifier)
            {
                case ImageProperty:
                    Image = stored ? new ImageReference(0, complex) : new ImageReference(value, -1);
                    return;
                case GeometryTextProperty when stored:
                    _geometryText = Text(table, complex, value);
                    return;
            }

            if (stored)
                return;

            switch (identifier)
            {
                case HorizontalProperty: Position = Position with { Horizontal = value }; return;
                case RelativeHorizontalProperty: Position = Position with { RelativeToHorizontal = value }; return;
                case VerticalProperty: Position = Position with { Vertical = value }; return;
                case RelativeVerticalProperty: Position = Position with { RelativeToVertical = value }; return;
                case RotationProperty: _rotation = value / FixedPoint; return;
                case FillColorProperty: _fillColor = value; return;
                case FillBooleansProperty: _fillBooleans = (uint)value; return;
                case LineColorProperty: _lineColor = value; return;
                case LineWidthProperty: _lineWidth = value; return;
                case LineBooleansProperty: _lineBooleans = (uint)value; return;
            }
        }

        /// <summary>
        /// Whether a fill or a line is drawn. The bit only counts when the bit beside it says
        /// it was set; a shape that says nothing at all takes the format's default, which for
        /// both of them is on.
        /// </summary>
        private static bool Drawn(uint booleans, uint on, uint stated, bool defaultOn) =>
            (booleans & stated) != 0 ? (booleans & on) != 0 : defaultOn;

        /// <summary>A stored string, which the drawing layer writes as UTF-16 with a trailing nul.</summary>
        private static string? Text(byte[] table, int at, int length)
        {
            if (length < 2 || at < 0 || at + length > table.Length)
                return null;

            string text = System.Text.Encoding.Unicode.GetString(table, at, length).TrimEnd('\0');
            return text.Length == 0 ? null : text.Replace('\u000B', '\n');
        }
    }
}
