using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Generates the <c>w:drawing</c> markup for a picture.
/// </summary>
/// <remarks>
/// A picture read from a file keeps its original markup, which preserves cropping, effects,
/// wrapping and the extension elements Word adds. An unchanged picture is written back
/// verbatim, and a changed one has the handful of attributes the model owns rewritten in
/// place — so resizing a floating picture leaves it floating. Markup is generated from
/// nothing only for a picture the caller created, or for one converted out of a format that
/// has no markup of its own.
/// </remarks>
internal static class DrawingWriter
{
    private static int _nextId;

    /// <summary>Writes the drawing for a picture, reusing its original markup when it is unchanged.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="picture">The picture to write.</param>
    /// <param name="relationshipId">Relationship id of the image part.</param>
    public static void Write(Utf8XmlWriter writer, Picture picture, string? relationshipId)
    {
        if (picture.OriginalXml is { } original)
        {
            if (!picture.IsDirty)
            {
                writer.WriteRawXml(original);
                return;
            }

            if (DrawingResizer.Rewrite(original, picture, relationshipId) is { } rewritten)
            {
                writer.WriteRawXml(rewritten);
                return;
            }
        }

        if (relationshipId is null)
            return;

        int id = Interlocked.Increment(ref _nextId);
        string name = picture.Name ?? $"Picture {id}";

        writer.WriteRaw("<w:drawing>"u8);
        if (picture.IsInline)
            WriteInlineFrame(writer, picture, id, name);
        else
            WriteAnchorFrame(writer, picture, id, name);

        WriteGraphic(writer, picture, relationshipId, id, name);
        writer.WriteRaw(picture.IsInline ? "</wp:inline></w:drawing>"u8 : "</wp:anchor></w:drawing>"u8);
    }

    private static void WriteInlineFrame(Utf8XmlWriter writer, Picture picture, int id, string name)
    {
        writer.WriteRaw("<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">"u8);
        WriteExtent(writer, picture);
        writer.WriteRaw("<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>"u8);
        WriteProperties(writer, picture, id, name);
    }

    /// <summary>
    /// The frame of a picture that floats: where it sits and what the text does about it. A
    /// picture that says nothing about either is anchored where its paragraph is with the text
    /// flowing around it, which is what a reader would draw anyway.
    /// </summary>
    private static void WriteAnchorFrame(Utf8XmlWriter writer, Picture picture, int id, string name)
    {
        PictureAnchor anchor = picture.Anchor ?? DefaultAnchor;

        writer.WriteRaw("<wp:anchor distT=\""u8);
        writer.WriteInt64(Math.Max(0, anchor.DistanceTop.Emu));
        writer.WriteRaw("\" distB=\""u8);
        writer.WriteInt64(Math.Max(0, anchor.DistanceBottom.Emu));
        writer.WriteRaw("\" distL=\""u8);
        writer.WriteInt64(Math.Max(0, anchor.DistanceLeft.Emu));
        writer.WriteRaw("\" distR=\""u8);
        writer.WriteInt64(Math.Max(0, anchor.DistanceRight.Emu));
        writer.WriteRaw("\" simplePos=\"0\" relativeHeight=\"0\""u8);
        writer.WriteRaw(anchor.BehindText ? " behindDoc=\"1\""u8 : " behindDoc=\"0\""u8);
        writer.WriteRaw(" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\">"u8);
        writer.WriteRaw("<wp:simplePos x=\"0\" y=\"0\"/>"u8);
        WritePosition(
            writer, "positionH"u8, Origin(anchor.HorizontalFrom, horizontal: true),
            Aligned(anchor.HorizontalAlignment, horizontal: true), anchor.OffsetX);
        WritePosition(
            writer, "positionV"u8, Origin(anchor.VerticalFrom, horizontal: false),
            Aligned(anchor.VerticalAlignment, horizontal: false), anchor.OffsetY);
        WriteExtent(writer, picture);
        writer.WriteRaw("<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>"u8);
        WriteWrap(writer, anchor);
        WriteProperties(writer, picture, id, name);
    }

    private static readonly PictureAnchor DefaultAnchor = new();

    private static void WritePosition(
        Utf8XmlWriter writer,
        ReadOnlySpan<byte> element,
        ReadOnlySpan<byte> from,
        ReadOnlySpan<byte> alignment,
        Primitives.Length offset)
    {
        writer.WriteRaw("<wp:"u8);
        writer.WriteRaw(element);
        writer.WriteRaw(" relativeFrom=\""u8);
        writer.WriteRaw(from);
        writer.WriteRaw("\">"u8);

        // A position is either a distance or an edge to line up with, never both.
        if (alignment.IsEmpty)
        {
            writer.WriteRaw("<wp:posOffset>"u8);
            writer.WriteInt32(Offset(offset));
            writer.WriteRaw("</wp:posOffset>"u8);
        }
        else
        {
            writer.WriteRaw("<wp:align>"u8);
            writer.WriteRaw(alignment);
            writer.WriteRaw("</wp:align>"u8);
        }

        writer.WriteRaw("</wp:"u8);
        writer.WriteRaw(element);
        writer.WriteRaw(">"u8);
    }

    /// <summary>
    /// The edge a picture lines up with, or nothing when it is placed at a distance instead.
    /// The two axes name their leading and trailing edges differently, and neither takes the
    /// other's name.
    /// </summary>
    private static ReadOnlySpan<byte> Aligned(AnchorAlignment alignment, bool horizontal) => alignment switch
    {
        AnchorAlignment.Start => horizontal ? "left"u8 : "top"u8,
        AnchorAlignment.Center => "center"u8,
        AnchorAlignment.End => horizontal ? "right"u8 : "bottom"u8,
        AnchorAlignment.Inside => "inside"u8,
        AnchorAlignment.Outside => "outside"u8,
        _ => default,
    };

    /// <summary>
    /// The element that says how the text behaves. Each kind of wrapping is its own element
    /// rather than an attribute, and only the ones that leave the text somewhere to go take
    /// the attribute saying which side it goes down.
    /// </summary>
    private static void WriteWrap(Utf8XmlWriter writer, PictureAnchor anchor)
    {
        if (anchor.Wrapping == TextWrapping.None)
        {
            writer.WriteRaw("<wp:wrapNone/>"u8);
            return;
        }

        if (anchor.Wrapping == TextWrapping.TopAndBottom)
        {
            writer.WriteRaw("<wp:wrapTopAndBottom/>"u8);
            return;
        }

        writer.WriteRaw("<wp:"u8);
        writer.WriteRaw(anchor.Wrapping switch
        {
            TextWrapping.Tight => "wrapTight"u8,
            TextWrapping.Through => "wrapThrough"u8,
            _ => "wrapSquare"u8,
        });

        writer.WriteRaw(" wrapText=\""u8);
        writer.WriteRaw(anchor.Sides switch
        {
            WrapSides.Left => "left"u8,
            WrapSides.Right => "right"u8,
            WrapSides.Largest => "largest"u8,
            _ => "bothSides"u8,
        });

        if (anchor.Wrapping is not (TextWrapping.Tight or TextWrapping.Through))
        {
            writer.WriteRaw("\"/>"u8);
            return;
        }

        // Wrapping that follows an outline has to say what the outline is: the one the anchor
        // carries, or the object's own rectangle when it carries none. The coordinates are the
        // twenty-one-thousand-six-hundredths of the object that this corner of the format
        // counts in.
        writer.WriteRaw("\"><wp:wrapPolygon edited=\"0\">"u8);

        if (anchor.Polygon.Count >= 3)
        {
            for (int i = 0; i < anchor.Polygon.Count; i++)
            {
                writer.WriteRaw(i == 0 ? "<wp:start x=\""u8 : "<wp:lineTo x=\""u8);
                writer.WriteInt64(anchor.Polygon[i].X);
                writer.WriteRaw("\" y=\""u8);
                writer.WriteInt64(anchor.Polygon[i].Y);
                writer.WriteRaw("\"/>"u8);
            }
        }
        else
        {
            writer.WriteRaw("<wp:start x=\"0\" y=\"0\"/>"u8);
            writer.WriteRaw("<wp:lineTo x=\"0\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"21600\"/>"u8);
            writer.WriteRaw("<wp:lineTo x=\"21600\" y=\"0\"/><wp:lineTo x=\"0\" y=\"0\"/>"u8);
        }

        writer.WriteRaw("</wp:wrapPolygon>"u8);
        writer.WriteRaw(anchor.Wrapping == TextWrapping.Tight ? "</wp:wrapTight>"u8 : "</wp:wrapThrough>"u8);
    }

    /// <summary>
    /// What an offset is measured from. The two axes take different names for the same idea,
    /// and neither takes the other's, so an origin that makes no sense on this axis falls back
    /// to the one that does.
    /// </summary>
    private static ReadOnlySpan<byte> Origin(AnchorOrigin origin, bool horizontal) => origin switch
    {
        AnchorOrigin.Page => "page"u8,
        AnchorOrigin.Margin => "margin"u8,
        AnchorOrigin.Column when horizontal => "column"u8,
        AnchorOrigin.Character when horizontal => "character"u8,
        AnchorOrigin.Paragraph when !horizontal => "paragraph"u8,
        AnchorOrigin.Line when !horizontal => "line"u8,
        _ => horizontal ? "column"u8 : "paragraph"u8,
    };

    private static void WriteExtent(Utf8XmlWriter writer, Picture picture)
    {
        writer.WriteRaw("<wp:extent"u8);
        WordXml.Attribute(writer, "cx"u8, Emu(picture.Width));
        WordXml.Attribute(writer, "cy"u8, Emu(picture.Height));
        writer.WriteRaw("/>"u8);
    }

    private static void WriteProperties(Utf8XmlWriter writer, Picture picture, int id, string name)
    {
        writer.WriteRaw("<wp:docPr"u8);
        WordXml.Attribute(writer, "id"u8, id);
        WordXml.Attribute(writer, "name"u8, name);
        WordXml.Attribute(writer, "descr"u8, picture.Description);
        writer.WriteRaw("/><wp:cNvGraphicFramePr><a:graphicFrameLocks xmlns:a=\""u8);
        writer.WriteRawXml(DocxSchema.NsDrawing);
        writer.WriteRaw("\" noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>"u8);
    }

    private static void WriteGraphic(Utf8XmlWriter writer, Picture picture, string relationshipId, int id, string name)
    {
        writer.WriteRaw("<a:graphic xmlns:a=\""u8);
        writer.WriteRawXml(DocxSchema.NsDrawing);
        writer.WriteRaw("\"><a:graphicData uri=\""u8);
        writer.WriteRawXml(DocxSchema.NsPicture);
        writer.WriteRaw("\"><pic:pic xmlns:pic=\""u8);
        writer.WriteRawXml(DocxSchema.NsPicture);
        writer.WriteRaw("\"><pic:nvPicPr><pic:cNvPr"u8);
        WordXml.Attribute(writer, "id"u8, id);
        WordXml.Attribute(writer, "name"u8, name);
        WordXml.Attribute(writer, "descr"u8, picture.Description);
        writer.WriteRaw("/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\""u8);
        writer.WriteAttributeText(relationshipId);
        writer.WriteRaw("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext"u8);
        WordXml.Attribute(writer, "cx"u8, Emu(picture.Width));
        WordXml.Attribute(writer, "cy"u8, Emu(picture.Height));
        writer.WriteRaw("/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic>"u8);
    }

    /// <summary>A size the drawing layer will accept: positive, and inside its range.</summary>
    private static int Emu(Primitives.Length value) => (int)Math.Clamp(value.Emu, 1, int.MaxValue);

    /// <summary>
    /// A position the drawing layer will accept, which unlike a size may be zero or to the
    /// left of where it is measured from.
    /// </summary>
    private static int Offset(Primitives.Length value) => (int)Math.Clamp(value.Emu, int.MinValue, int.MaxValue);
}
