using System.Buffers.Binary;
using System.Text;
using Quillwright.Primitives;

namespace Quillwright.Doc.Tests;

/// <summary>
/// What a drawing says about how it looks ([MS-ODRAW] 2.3), read out of a shape's property
/// table.
/// </summary>
/// <remarks>
/// The properties covered here are the ones the reference corpus actually contains — the sweep
/// that decided which those are is in <see cref="OfficeArtInventoryTests"/>. Each record is
/// built here byte by byte from the specification's layout, so a test passes because the reader
/// agrees with [MS-ODRAW] rather than with a writer that shares its assumptions.
/// </remarks>
public class OfficeArtAppearanceTests
{
    private const ushort DrawingGroup = 0xF000;
    private const ushort Drawing = 0xF002;
    private const ushort ShapeGroup = 0xF003;
    private const ushort ShapeContainer = 0xF004;
    private const ushort ShapeProperties = 0xF00A;
    private const ushort PrimaryOptions = 0xF00B;

    /// <summary>The bits a shape sets to say a fill or a line is drawn, and that it means it.</summary>
    private const uint FillOnAndStated = 0x00100010;
    private const uint FillOffAndStated = 0x00100000;
    private const uint LineOnAndStated = 0x00080008;
    private const uint LineOffAndStated = 0x00080000;

    [Fact]
    public void AShape_KeepsThePresetItWasDrawnAs()
    {
        Assert.Equal(202, Shape(shapeType: 202).ShapeType);
        Assert.Equal("a text box", Shape(shapeType: 202).TypeName);
        Assert.Equal("a rectangle", Shape(shapeType: 1).TypeName);
        Assert.Equal("shape type 99", Shape(shapeType: 99).TypeName);
    }

    /// <summary>
    /// The drawing layer writes a colour blue first, which is the opposite way round from
    /// everywhere else in either format — so a reader that copies the bytes across gets the
    /// red and the blue the wrong way about and nothing ever says so.
    /// </summary>
    [Fact]
    public void AFillColour_IsReadBlueFirst()
    {
        OfficeArtShape shape = Shape(properties:
        [
            (0x01BF, FillOnAndStated),
            (0x0181, 0x00CC8844),
        ]);

        Assert.Equal(WordColor.FromRgb(0x4488CC), shape.Appearance.Fill);
    }

    /// <summary>
    /// A colour and a bit saying whether it is drawn are two properties, and the colour means
    /// nothing without the bit: a shape that states a fill colour and then turns the fill off
    /// shows the page behind it.
    /// </summary>
    [Fact]
    public void AFillColourWithTheFillTurnedOff_IsNotAFill()
    {
        OfficeArtShape shape = Shape(properties:
        [
            (0x01BF, FillOffAndStated),
            (0x0181, 0x00CC8844),
        ]);

        Assert.Null(shape.Appearance.Fill);
    }

    /// <summary>A shape that says nothing takes the format's defaults, which are filled and lined.</summary>
    [Fact]
    public void AShapeThatSaysNothingAboutItsFill_TakesTheDefault()
    {
        OfficeArtShape shape = Shape(properties: [(0x0181, 0x00000000)]);

        Assert.Equal(WordColor.FromRgb(0x000000), shape.Appearance.Fill);
    }

    [Fact]
    public void ALine_KeepsItsColourAndItsThickness()
    {
        OfficeArtShape shape = Shape(properties:
        [
            (0x01C0, 0x00000080),

            // The width is in English metric units, of which a point is 12 700.
            (0x01CB, 12700 * 2),
            (0x01FF, LineOnAndStated),
        ]);

        Assert.Equal(WordColor.FromRgb(0x800000), shape.Appearance.LineColor);
        Assert.Equal(2, shape.Appearance.LineWidth!.Value.Points, 2);
    }

    [Fact]
    public void ALineTurnedOff_IsNoLine()
    {
        OfficeArtShape shape = Shape(properties: [(0x01C0, 0x00000080), (0x01FF, LineOffAndStated)]);

        Assert.Null(shape.Appearance.LineColor);
        Assert.Null(shape.Appearance.LineWidth);
    }

    /// <summary>
    /// A colour taken from a scheme or from the system palette is a name rather than a colour,
    /// and this reader carries no palette to look it up in.
    /// </summary>
    [Theory]
    [InlineData(0x08000004u)]
    [InlineData(0x10000018u)]
    public void AColourThatIsAnIndexRatherThanAColour_IsNotGuessedAt(uint value)
    {
        OfficeArtShape shape = Shape(properties: [(0x01BF, FillOnAndStated), (0x0181, value)]);

        Assert.Null(shape.Appearance.Fill);
    }

    /// <summary>Rotation is a fixed-point number with sixteen bits of fraction.</summary>
    [Fact]
    public void ARotation_ComesBackInDegrees()
    {
        Assert.Equal(90, Shape(properties: [(0x0004, 90u << 16)]).Appearance.Rotation, 3);
        Assert.Equal(-45, Shape(properties: [(0x0004, unchecked((uint)(-45 << 16)))]).Appearance.Rotation, 3);
    }

    /// <summary>
    /// A WordArt shape keeps its words in a property and nowhere else, so a reader that skips
    /// the property loses them. The value is stored after the table, and a line break inside it
    /// is a vertical tab.
    /// </summary>
    [Fact]
    public void Lettering_IsReadOutOfThePropertyItIsStoredIn()
    {
        byte[] text = Encoding.Unicode.GetBytes("Draft\u000BCopy\0");
        OfficeArtShape shape = Shape(properties: [(0x00C0, (uint)text.Length)], complex: text, complexProperty: 0x00C0);

        Assert.Equal("Draft\nCopy", shape.Appearance.GeometryText);
    }

    /// <summary>Builds the drawing region of a table stream holding exactly one shape.</summary>
    private static OfficeArtShape Shape(
        int shapeType = 1,
        (int Identifier, uint Value)[]? properties = null,
        byte[]? complex = null,
        int complexProperty = -1)
    {
        properties ??= [];
        var options = new List<byte>();

        foreach ((int identifier, uint value) in properties)
        {
            int header = identifier == complexProperty ? identifier | 0x8000 : identifier;
            options.AddRange(Bytes((ushort)header));
            options.AddRange(BitConverter.GetBytes(value));
        }

        options.AddRange(complex ?? []);

        byte[] shape = Container(ShapeContainer,
        [
            .. Record(ShapeProperties, instance: shapeType, version: 2, [.. BitConverter.GetBytes(1), .. BitConverter.GetBytes(0x0A00)]),
            .. Record(PrimaryOptions, instance: properties.Length, version: 3, [.. options]),
        ]);

        byte[] region =
        [
            .. Container(DrawingGroup, []),
            0,
            .. Container(Drawing, [.. Container(ShapeGroup, [.. shape])]),
        ];

        return OfficeArtShapes.Read(region, (0, region.Length)).ById(1)!.Value;
    }

    private static byte[] Container(ushort type, byte[] body) => Record(type, instance: 0, version: 0xF, body);

    private static byte[] Record(ushort type, int instance, int version, byte[] body)
    {
        byte[] record = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(record, (ushort)((instance << 4) | version));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(2), type);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), body.Length);
        body.CopyTo(record.AsSpan(8));
        return record;
    }

    private static byte[] Bytes(ushort value)
    {
        byte[] pair = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(pair, value);
        return pair;
    }
}
