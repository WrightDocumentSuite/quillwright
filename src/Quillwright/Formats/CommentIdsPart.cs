using System.Globalization;
using System.Xml;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Reads and writes the durable comment identifiers Word 2016 added ([MS-DOCX] 2.8,
/// <c>commentsIds.xml</c>).
/// </summary>
/// <remarks>
/// <para>
/// A comment's <c>w:id</c> is only an index within one save; renumbering it is legal and Word
/// does it. The durable identifier is the one that survives, which is what lets two people
/// editing the same document at once tell whether they are looking at the same comment.
/// </para>
/// <para>
/// Like the threading part, this one names comments by the paragraph identifier of their last
/// paragraph rather than by comment id, so the two are prepared together.
/// </para>
/// </remarks>
internal static class CommentIdsPart
{
    /// <summary>Attaches the durable identifiers to the comments already read.</summary>
    /// <param name="xml">Reader over the part.</param>
    /// <param name="document">The document being loaded.</param>
    public static void Read(XmlReader xml, WordDocument document)
    {
        Dictionary<string, Comment> byParagraphId = CommentThreadReader.ByParagraphId(document);
        if (byParagraphId.Count == 0)
            return;

        StylesPartReader.MoveToRoot(xml, "commentsIds");
        if (xml.NodeType != XmlNodeType.Element)
            return;

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name != "commentId")
            {
                reader.Skip();
                return;
            }

            if (Attribute(reader, "paraId") is { } paragraphId &&
                byParagraphId.TryGetValue(paragraphId, out Comment? comment))
            {
                comment.DurableId = Attribute(reader, "durableId");
            }

            reader.Skip();
        });
    }

    /// <summary>The part holding durable identifiers, or <see langword="null"/> when there is none.</summary>
    /// <param name="preserved">The package as loaded.</param>
    public static string? FindPart(PreservedPackage preserved)
    {
        OpcRelationship relationship = preserved.MainRelationships.FirstOrDefault(
            static r => r.Is(DocxSchema.RelCommentsIds));
        return relationship.Target is null ? null : OpcPath.Resolve(preserved.MainPartPath, relationship.Target);
    }

    /// <summary>
    /// Settles the durable identifier of every comment that the parts naming them can reach:
    /// the one it came in with where that is usable, a fresh one otherwise.
    /// </summary>
    /// <param name="document">The document being written.</param>
    /// <param name="identifiers">The paragraph identifiers the threading writer settled on.</param>
    /// <remarks>
    /// This part and <see cref="CommentExtensiblePart"/> both name comments by durable
    /// identifier, so they have to agree on it or the metadata of one comment lands on
    /// another. The map is built once, before either is written.
    /// </remarks>
    public static Dictionary<Comment, string> Prepare(WordDocument document, IReadOnlyDictionary<Comment, string> identifiers)
    {
        var durable = new Dictionary<Comment, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Comment comment in document.Comments)
        {
            // A comment with no paragraph identifier cannot be named by the identifiers part,
            // and the two parts have to describe the same set of comments.
            if (identifiers.ContainsKey(comment))
                durable[comment] = Durable(comment, used);
        }

        return durable;
    }

    /// <summary>Writes the part.</summary>
    /// <param name="writer">The part's writer.</param>
    /// <param name="document">The document being written.</param>
    /// <param name="identifiers">The paragraph identifiers the threading writer settled on.</param>
    /// <param name="durable">The durable identifiers <see cref="Prepare"/> settled on.</param>
    public static void Write(
        Utf8XmlWriter writer,
        WordDocument document,
        IReadOnlyDictionary<Comment, string> identifiers,
        IReadOnlyDictionary<Comment, string> durable)
    {
        writer.WriteDeclaration();
        writer.WriteRaw("<w16cid:commentsIds xmlns:w16cid=\""u8);
        writer.WriteRawXml(DocxSchema.NsW16Cid);
        writer.WriteRaw("\" xmlns:mc=\""u8);
        writer.WriteRawXml(DocxSchema.NsMarkupCompatibility);
        writer.WriteRaw("\" mc:Ignorable=\"w16cid\">"u8);

        foreach (Comment comment in document.Comments)
        {
            if (!identifiers.TryGetValue(comment, out string? paragraphId) ||
                !durable.TryGetValue(comment, out string? durableId))
            {
                continue;
            }

            writer.WriteRaw("<w16cid:commentId w16cid:paraId=\""u8);
            writer.WriteAttributeText(paragraphId);
            writer.WriteRaw("\" w16cid:durableId=\""u8);
            writer.WriteAttributeText(durableId);
            writer.WriteRaw("\"/>"u8);
        }

        writer.WriteRaw("</w16cid:commentsIds>"u8);
    }

    /// <summary>
    /// The durable identifier of a comment: the one it came in with where that is usable, and
    /// a fresh one otherwise. Values have to be greater than zero and less than 0x7FFFFFFF.
    /// </summary>
    /// <param name="comment">The comment being written.</param>
    /// <param name="used">Identifiers already spoken for in this part.</param>
    private static string Durable(Comment comment, HashSet<string> used)
    {
        if (comment.DurableId is { Length: > 0 } existing &&
            uint.TryParse(existing, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value) &&
            value is > 0 and < 0x7FFFFFFF &&
            used.Add(existing))
        {
            return existing;
        }

        uint next = 0x20000000;
        string id;
        do
        {
            id = (next++).ToString("X8", CultureInfo.InvariantCulture);
        }
        while (!used.Add(id));

        comment.DurableId = id;
        return id;
    }

    private static string? Attribute(XmlReader xml, string name) =>
        xml.GetAttribute(name, DocxSchema.NsW16Cid) ?? xml.GetAttribute("w16cid:" + name);
}
