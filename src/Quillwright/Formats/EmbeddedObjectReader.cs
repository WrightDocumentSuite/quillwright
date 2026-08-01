using System.Xml;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// Recognises an embedded object in the body markup (<c>w:object</c>) and resolves what the
/// package holds for it.
/// </summary>
/// <remarks>
/// The markup itself is preserved verbatim and is not rebuilt from this; reading it only adds
/// a typed view, so that a caller can find out what a document carries and pull an attachment
/// out of it. The element names two relationships: one to the object's own part, and one to
/// the picture Word caches of it.
/// </remarks>
internal static class EmbeddedObjectReader
{
    /// <summary>Reads what a <c>w:object</c> element points at, or nothing when it points at nothing.</summary>
    /// <param name="markup">The whole element as it was read.</param>
    /// <param name="context">The load in progress.</param>
    public static EmbeddedObject? Read(string markup, LoadContext context)
    {
        (string? objectId, string? programId, bool isLinked, string? previewId) = ReadReferences(markup);

        // The element is only believed when the relationship it names really is one to an
        // object: a file that reuses an id for something else must not turn settings.xml into
        // an attachment.
        if (context.Preserved.FindRelationship(context.SourcePart, objectId) is not { } relationship ||
            !(relationship.Is(DocxSchema.RelOleObject) || relationship.Is(DocxSchema.RelPackage)) ||
            context.Preserved.ResolveTarget(context.SourcePart, objectId) is not { } path ||
            context.Preserved.Parts.GetValueOrDefault(path) is not { Length: > 0 } content)
            return null;

        OleDescription? description = OleContainer.Describe(content);
        return new EmbeddedObject
        {
            Location = path,
            ProgramId = programId ?? description?.ProgramId,
            DisplayName = description?.DisplayName,
            IsLinked = isLinked || description?.IsLinked == true,
            Content = content,
            PackagedFileName = description?.PackagedFileName,
            PackagedFile = description?.PackagedFile ?? ReadOnlyMemory<byte>.Empty,
            Preview = previewId is null ? null : context.ImageFor(previewId),
        };
    }

    /// <summary>
    /// The two relationship ids and the program identifier, taken from the element without
    /// building a tree for it.
    /// </summary>
    private static (string? ObjectId, string? ProgramId, bool IsLinked, string? PreviewId) ReadReferences(string markup)
    {
        string? objectId = null;
        string? programId = null;
        string? previewId = null;
        bool isLinked = false;

        using var xml = XmlReader.Create(new StringReader(markup), Xml.XmlDefaults.ReaderSettings);
        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element)
                continue;

            switch (xml.LocalName)
            {
                case "OLEObject":
                    objectId = XmlHelp.RelAttr(xml);
                    programId = xml.GetAttribute("ProgID");
                    isLinked = string.Equals(xml.GetAttribute("Type"), "Link", StringComparison.OrdinalIgnoreCase);
                    break;
                case "imagedata":
                    previewId ??= XmlHelp.RelAttr(xml);
                    break;
            }
        }

        return (objectId, programId, isLinked, previewId);
    }
}
