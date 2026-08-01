using System.Globalization;
using System.Xml;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Reads and writes the comment metadata Word 2018 added ([MS-DOCX] 2.10,
/// <c>commentsExtensible.xml</c>).
/// </summary>
/// <remarks>
/// <para>
/// The part holds what the comments part itself has no room for: an unambiguous timestamp,
/// the flag that marks a comment as a follow-up prompt rather than a remark, and — inside an
/// extension list this version does not interpret — the reactions of [MS-OREACTXML].
/// </para>
/// <para>
/// Unlike the threading part it names comments neither by id nor by paragraph identifier but
/// by the durable identifier of <c>commentsIds.xml</c>, so the two are settled together and
/// must agree: a durable identifier that drifts detaches a comment from its reactions.
/// </para>
/// </remarks>
internal static class CommentExtensiblePart
{
    /// <summary>Attaches the extended metadata to the comments already read.</summary>
    /// <param name="xml">Reader over the part.</param>
    /// <param name="document">The document being loaded.</param>
    public static void Read(XmlReader xml, WordDocument document)
    {
        Dictionary<string, Comment> byDurableId = ByDurableId(document);
        if (byDurableId.Count == 0)
            return;

        StylesPartReader.MoveToRoot(xml, "commentsExtensible");
        if (xml.NodeType != XmlNodeType.Element)
            return;

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name != "commentExtensible")
            {
                reader.Skip();
                return;
            }

            Comment? comment = Attribute(reader, "durableId") is { } durableId &&
                byDurableId.TryGetValue(durableId, out Comment? found)
                    ? found
                    : null;

            if (comment is not null)
            {
                comment.DateUtc = XmlHelp.ParseDate(Attribute(reader, "dateUtc"))?.ToUniversalTime();
                comment.IsFollowUp = XmlHelp.ParseOnOff(Attribute(reader, "intelligentPlaceholder")) ?? false;
            }

            XmlHelp.ForEachChild(reader, (child, childName) =>
            {
                if (comment is not null && childName == "extLst")
                    comment.ExtensibleExtLstXml = child.ReadOuterXml();
                else
                    child.Skip();
            });
        });
    }

    /// <summary>The part holding the extended metadata, or <see langword="null"/> when there is none.</summary>
    /// <param name="preserved">The package as loaded.</param>
    public static string? FindPart(PreservedPackage preserved)
    {
        OpcRelationship relationship = preserved.MainRelationships.FirstOrDefault(
            static r => r.Is(DocxSchema.RelCommentsExtensible));
        return relationship.Target is null ? null : OpcPath.Resolve(preserved.MainPartPath, relationship.Target);
    }

    /// <summary>Whether a document has anything to say in this part beyond what it was loaded with.</summary>
    /// <param name="document">The document being written.</param>
    public static bool HasMetadata(WordDocument document) =>
        document.Comments.Any(static comment =>
            comment.DateUtc is not null || comment.IsFollowUp || comment.ExtensibleExtLstXml is not null);

    /// <summary>Writes the part.</summary>
    /// <param name="writer">The part's writer.</param>
    /// <param name="document">The document being written.</param>
    /// <param name="durable">The durable identifiers <see cref="CommentIdsPart.Prepare"/> settled on.</param>
    /// <remarks>
    /// Entries are emitted by walking the model, so a comment that is no longer there loses
    /// its entry rather than leaving one behind pointing at a durable identifier nothing
    /// carries any more.
    /// </remarks>
    public static void Write(Utf8XmlWriter writer, WordDocument document, IReadOnlyDictionary<Comment, string> durable)
    {
        // The extension lists carried through below are written in w16 and, for a reaction, in
        // cr; both prefixes are declared here so a captured fragment that leans on the part's
        // own namespace context still resolves.
        writer.WriteDeclaration();
        writer.WriteRaw("<w16cex:commentsExtensible xmlns:w16cex=\""u8);
        writer.WriteRawXml(DocxSchema.NsW16Cex);
        writer.WriteRaw("\" xmlns:w16=\""u8);
        writer.WriteRawXml(DocxSchema.NsW16);
        writer.WriteRaw("\" xmlns:cr=\""u8);
        writer.WriteRawXml(DocxSchema.NsReactions);
        writer.WriteRaw("\" xmlns:mc=\""u8);
        writer.WriteRawXml(DocxSchema.NsMarkupCompatibility);
        writer.WriteRaw("\" mc:Ignorable=\"w16cex w16 cr\">"u8);

        foreach (Comment comment in document.Comments)
        {
            if (!durable.TryGetValue(comment, out string? durableId))
                continue;

            writer.WriteRaw("<w16cex:commentExtensible w16cex:durableId=\""u8);
            writer.WriteAttributeText(durableId);
            writer.WriteRaw("\""u8);

            if ((comment.DateUtc ?? comment.Date?.ToUniversalTime()) is { } date)
            {
                writer.WriteRaw(" w16cex:dateUtc=\""u8);
                writer.WriteRawXml(date.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                writer.WriteRaw("\""u8);
            }

            // A reply is answering something, so it cannot itself be the prompt that asks for
            // an answer ([MS-DOCX] 2.10.3.1).
            if (comment.IsFollowUp && comment.ParentId is null)
                writer.WriteRaw(" w16cex:intelligentPlaceholder=\"1\""u8);

            if (comment.ExtensibleExtLstXml is { Length: > 0 } extensions)
            {
                writer.WriteRaw(">"u8);
                writer.WriteRawXml(extensions);
                writer.WriteRaw("</w16cex:commentExtensible>"u8);
            }
            else
            {
                writer.WriteRaw("/>"u8);
            }
        }

        writer.WriteRaw("</w16cex:commentsExtensible>"u8);
    }

    /// <summary>The comments of a document that carry a durable identifier, keyed by it.</summary>
    private static Dictionary<string, Comment> ByDurableId(WordDocument document)
    {
        var byDurableId = new Dictionary<string, Comment>(StringComparer.OrdinalIgnoreCase);
        foreach (Comment comment in document.Comments)
        {
            if (comment.DurableId is { Length: > 0 } id)
                byDurableId[id] = comment;
        }

        return byDurableId;
    }

    private static string? Attribute(XmlReader xml, string name) =>
        xml.GetAttribute(name, DocxSchema.NsW16Cex) ?? xml.GetAttribute("w16cex:" + name);
}
