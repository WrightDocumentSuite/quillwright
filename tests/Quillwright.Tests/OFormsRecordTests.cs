using Quillwright.Vba;
using Quillwright.Vba.OForms;

namespace Quillwright.Tests;

/// <summary>
/// Reads the records the specification works through by hand ([MS-OFORMS] section 3), so the
/// reader is checked against Microsoft's own worked examples and not only against what Word
/// happens to write.
/// </summary>
public class OFormsRecordTests
{
    /// <summary>
    /// [MS-OFORMS] 3.1. A string whose characters all have a zero high byte is stored with
    /// those bytes dropped; one that has any is stored as it stands.
    /// </summary>
    [Fact]
    public void ACompressedString_IsWidenedBackOut()
    {
        var reader = new OFormsReader([0x41, 0x42, 0x43], 0, 3);

        Assert.Equal("ABC", reader.Text(3, compressed: true));
    }

    [Fact]
    public void AnUncompressedString_IsReadAsItStands()
    {
        // The Japanese for "Earth", whose characters have no zero high byte to drop.
        var reader = new OFormsReader([0x30, 0x57, 0x03, 0x74], 0, 4);

        Assert.Equal("\u5730\u7403", reader.Text(4, compressed: false));
    }

    /// <summary>
    /// [MS-OFORMS] 3.2: a command button whose caption, size, mouse pointer and mouse icon are
    /// set. Only four bits of the mask are on, and the bytes between them are padding, so a
    /// reader that gets the alignment wrong reads the size out of the caption.
    /// </summary>
    [Fact]
    public void TheWorkedCommandButton_IsReadAsTheSpecificationDescribesIt()
    {
        byte[] record =
        [
            0x00, 0x02,             // MinorVersion, MajorVersion
            0x24, 0x00,             // cbCommandButton: mask plus both blocks
            0x68, 0x04, 0x00, 0x00, // PropMask: fCaption, fSize, fMousePointer, fMouseIcon
            0x0E, 0x00, 0x00, 0x80, // Caption: fourteen bytes, compressed
            0x63,                   // MousePointer
            0x00,                   // Padding, so the marker below starts on an even offset
            0xFF, 0xFF,             // MouseIcon: the value itself is in the stream data
            .. "CommandButton1"u8,
            0x00, 0x00,             // Padding, so the size below starts on a multiple of four
            0x5D, 0x11, 0x00, 0x00, // Width: 4445 hundredths of a millimetre, or 126 points
            0xF6, 0x04, 0x00, 0x00, // Height: 1270, or 36 points
        ];

        OFormsValues values = OFormsPropertyBag.Read(new OFormsReader(record, 0, record.Length), OFormsSchemas.CommandButton);

        Assert.Equal("CommandButton1", values.Text(OFormsSchemas.Caption));
        Assert.Equal((4445, 1270), values.Pair(OFormsSchemas.Size));
        Assert.Equal(0x63u, values.Number("MousePointer"));
        Assert.Equal(0x28, values.End);
    }

    /// <summary>
    /// [MS-OFORMS] 3.4: the entry a form keeps for one embedded control. The class index is
    /// 0x8000, which does not name a control the format knows but the first entry of the
    /// form's own class table, so the kind is left unknown while the rest is still read.
    /// </summary>
    [Fact]
    public void TheWorkedSite_IsReadAsTheSpecificationDescribesIt()
    {
        byte[] record =
        [
            0x00, 0x00,             // Version
            0x24, 0x00,             // cbSite
            0xE5, 0x01, 0x00, 0x00, // PropMask: fName, fID, fObjectStreamSize, fTabIndex, fClsidCacheIndex, fPosition
            0x08, 0x00, 0x00, 0x80, // Name: eight bytes, compressed
            0x01, 0x00, 0x00, 0x00, // ID
            0x38, 0x00, 0x00, 0x00, // ObjectStreamSize
            0x00, 0x00,             // TabIndex
            0x00, 0x80,             // ClsidCacheIndex: an entry of the class table, not a known control
            .. "RefEdit1"u8,
            0x45, 0x08, 0x00, 0x00, // Left
            0x9D, 0x06, 0x00, 0x00, // Top
        ];

        OFormsValues values = OFormsPropertyBag.Read(new OFormsReader(record, 0, record.Length), OFormsSchemas.Site);

        Assert.Equal("RefEdit1", values.Text(OFormsSchemas.Name));
        Assert.Equal(1u, values.Number(OFormsSchemas.Id));
        Assert.Equal(0x38u, values.Number(OFormsSchemas.ObjectStreamSize));
        Assert.Equal(0x8000u, values.Number(OFormsSchemas.ClsidCacheIndex));
        Assert.Equal((0x845, 0x69D), values.Pair(OFormsSchemas.Position));
        Assert.Equal(VbaFormControlKind.Unknown, OFormsControlKind.FromCacheIndex(0x8000));
    }

    /// <summary>
    /// A record that claims more room than the stream holds is read to the end and no
    /// further. Word writes forms whose last property runs a byte or two past the end, and
    /// refusing them would mean refusing the form.
    /// </summary>
    [Fact]
    public void ARecordThatRunsPastTheEnd_LeavesItsLastPropertyAtZero()
    {
        byte[] record =
        [
            0x00, 0x00,
            0x14, 0x00,             // cbSite claims twenty bytes of mask and blocks
            0x01, 0x01, 0x00, 0x00, // PropMask: fName and fPosition
            0x04, 0x00, 0x00, 0x80, // Name: four bytes, compressed
            .. "Stub"u8,
            0x2A, 0x00, 0x00, 0x00, // Left
            0x0B, 0x00,             // Top, cut two bytes short
        ];

        OFormsValues values = OFormsPropertyBag.Read(new OFormsReader(record, 0, record.Length), OFormsSchemas.Site);

        Assert.Equal("Stub", values.Text(OFormsSchemas.Name));
        Assert.Equal((0x2A, 0x0B), values.Pair(OFormsSchemas.Position));
    }
}
