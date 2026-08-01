using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Formats;

/// <summary>
/// Recognises the common shape of a <c>w:drawing</c>: one picture, sized, positioned, pointing
/// at an image part.
/// </summary>
/// <remarks>
/// The markup is kept verbatim either way. Parsing it only adds a typed view so that callers can
/// read the image, resize it and see where it sits; anything more elaborate — a chart, a canvas,
/// a group of shapes, a picture with cropping and effects — stays as the bytes it arrived as and
/// is written back unchanged.
/// </remarks>
internal static class DrawingReader
{
    /// <summary>Returns a picture when the drawing holds exactly one, otherwise <see langword="null"/>.</summary>
    public static Picture? Parse(string markup, LoadContext context)
    {
        DrawingGeometry found = DrawingGeometry.Read(markup);
        if (!found.ShowsOnePicture || found.RelationshipId is not { } relationshipId)
            return null;

        if (context.ImageFor(relationshipId) is not { } image)
        {
            context.Warn(Diagnostics.WarningCode.UnresolvedMedia, $"A picture refers to '{relationshipId}', which is not an image part.");
            return null;
        }

        var picture = new Picture
        {
            Image = image,
            Width = found.Width > 0 ? Length.FromEmu(found.Width) : image.NaturalWidth,
            Height = found.Height > 0 ? Length.FromEmu(found.Height) : image.NaturalHeight,
            Name = found.Name,
            Description = found.Description,
            IsInline = found.IsInline || !found.IsAnchored,
            Anchor = found.Anchor,
            OriginalXml = markup,
        };

        // Those properties describe the markup we already hold, so the picture starts clean
        // and keeps its original bytes until a caller actually changes something.
        picture.IsDirty = false;
        return picture;
    }
}
