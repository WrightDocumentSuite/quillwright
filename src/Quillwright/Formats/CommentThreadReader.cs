using System.Xml;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// Reads the comment threading Word 2013 added ([MS-DOCX] 2.1.2, <c>commentsExtended.xml</c>).
/// </summary>
/// <remarks>
/// The threading part does not use comment ids. It keys off the paragraph identifier of the
/// last paragraph of each comment, and names the parent comment by the same kind of
/// identifier, so replies and resolved state have to be matched through those ids.
/// </remarks>
internal static class CommentThreadReader
{
    /// <summary>
    /// The comments of a document, keyed by the paragraph identifier the parts that describe
    /// them name them by, recording that identifier on each comment as it goes.
    /// </summary>
    /// <param name="document">The document being loaded.</param>
    public static Dictionary<string, Comment> ByParagraphId(WordDocument document)
    {
        Dictionary<string, Comment> byParagraphId = new(StringComparer.OrdinalIgnoreCase);
        foreach (Comment comment in document.Comments)
        {
            if (LastParagraphId(comment) is { } id)
            {
                comment.ParagraphId = id;
                byParagraphId[id] = comment;
            }
        }

        return byParagraphId;
    }

    /// <summary>Applies threading information to the comments already read.</summary>
    /// <param name="xml">Reader over the part.</param>
    /// <param name="document">The document being loaded.</param>
    public static void Apply(XmlReader xml, WordDocument document)
    {
        Dictionary<string, Comment> byParagraphId = ByParagraphId(document);
        if (byParagraphId.Count == 0)
            return;

        StylesPartReader.MoveToRoot(xml, "commentsEx");
        if (xml.NodeType != XmlNodeType.Element)
            return;

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name != "commentEx")
            {
                reader.Skip();
                return;
            }

            string? paragraphId = Attribute(reader, "paraId");
            if (paragraphId is not null && byParagraphId.TryGetValue(paragraphId, out Comment? comment))
            {
                comment.IsResolved = XmlHelp.ParseOnOff(Attribute(reader, "done")) ?? false;
                if (Attribute(reader, "paraIdParent") is { } parentId &&
                    byParagraphId.TryGetValue(parentId, out Comment? parent))
                {
                    comment.ParentId = parent.Id;
                }
            }

            reader.Skip();
        });
    }

    /// <summary>The part holding comment threading, or <see langword="null"/> when there is none.</summary>
    public static string? FindPart(PreservedPackage preserved)
    {
        OpcRelationship relationship = preserved.MainRelationships.FirstOrDefault(
            static r => r.Is(DocxSchema.RelCommentsExtended));
        return relationship.Target is null ? null : OpcPath.Resolve(preserved.MainPartPath, relationship.Target);
    }

    private static string? Attribute(XmlReader xml, string name) =>
        xml.GetAttribute(name, DocxSchema.NsW15) ?? xml.GetAttribute("w15:" + name);

    private static string? LastParagraphId(Comment comment) =>
        comment.Blocks.Paragraphs.LastOrDefault() is { Attributes: { } attributes }
            ? ExtractParagraphId(attributes)
            : null;

    /// <summary>Pulls <c>w14:paraId</c> out of a captured start tag.</summary>
    private static string? ExtractParagraphId(string attributes)
    {
        int marker = attributes.IndexOf("paraId=\"", StringComparison.Ordinal);
        if (marker < 0)
            return null;

        int start = marker + "paraId=\"".Length;
        int end = attributes.IndexOf('"', start);
        return end < 0 ? null : attributes[start..end];
    }
}
