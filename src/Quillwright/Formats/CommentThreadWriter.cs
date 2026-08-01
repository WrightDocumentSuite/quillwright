using System.Globalization;
using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Writes the comment threading Word 2013 added ([MS-DOCX] 2.1.2, <c>commentsExtended.xml</c>).
/// </summary>
/// <remarks>
/// The part does not use comment ids. It names each comment by the paragraph identifier of
/// the comment's last paragraph, and names a reply's parent the same way, so a comment can
/// only take part in a thread once its last paragraph has an identifier. Comments read from a
/// package already have one; comments the caller added do not, and are given one here.
/// </remarks>
internal static class CommentThreadWriter
{
    /// <summary>Whether a document has anything to say in this part.</summary>
    /// <param name="document">The document being written.</param>
    public static bool HasThreads(WordDocument document) =>
        document.Comments.Any(static comment => comment.ParentId is not null || comment.IsResolved);

    /// <summary>
    /// Gives every comment's last paragraph an identifier, keeping the ones a loaded package
    /// already had so that markup preserved elsewhere still points at the right paragraph.
    /// This has to happen before the comments themselves are written, because the identifier
    /// is an attribute of the paragraph as well as the key this part uses.
    /// </summary>
    /// <param name="document">The document being written.</param>
    /// <param name="required">
    /// Whether identifiers are needed even with no threading to record, which is the case when
    /// the package carries the durable identifiers part and that has to be rewritten.
    /// </param>
    public static Dictionary<Comment, string> Prepare(WordDocument document, bool required = false)
    {
        var identifiers = new Dictionary<Comment, string>();
        if (!required && !HasThreads(document))
            return identifiers;

        // An identifier has to be unique across the whole part ([MS-DOCX] 2.6.2.3), so the
        // paragraphs of a comment that are not its last one are claimed here too, even though
        // this part never names them.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Comment other in document.Comments)
        {
            foreach (Paragraph paragraph in other.Blocks.Paragraphs.SkipLast(1))
            {
                if (ParagraphId(paragraph.Attributes) is { } claimed)
                    used.Add(claimed);
            }
        }

        uint next = 0x10000000;

        foreach (Comment comment in document.Comments)
        {
            if (comment.Blocks.Paragraphs.LastOrDefault() is not { } last)
                continue;

            string? id = ParagraphId(last.Attributes);
            if (id is null || !used.Add(id))
            {
                do
                {
                    id = (++next).ToString("X8", CultureInfo.InvariantCulture);
                }
                while (!used.Add(id));

                last.Attributes = Without(last.Attributes) is { } rest
                    ? $" w14:paraId=\"{id}\"{rest}"
                    : $" w14:paraId=\"{id}\"";
            }

            comment.ParagraphId = id;
            identifiers[comment] = id;
        }

        return identifiers;
    }

    /// <summary>Writes the part.</summary>
    /// <param name="writer">The part's writer.</param>
    /// <param name="document">The document being written.</param>
    /// <param name="identifiers">The paragraph identifiers <see cref="Prepare"/> settled on.</param>
    public static void Write(Utf8XmlWriter writer, WordDocument document, IReadOnlyDictionary<Comment, string> identifiers)
    {
        writer.WriteDeclaration();
        writer.WriteRaw("<w15:commentsEx xmlns:w=\""u8);
        writer.WriteRawXml(DocxSchema.NsWord);
        writer.WriteRaw("\" xmlns:w14=\""u8);
        writer.WriteRawXml(DocxSchema.NsW14);
        writer.WriteRaw("\" xmlns:w15=\""u8);
        writer.WriteRawXml(DocxSchema.NsW15);
        writer.WriteRaw("\" mc:Ignorable=\"w14 w15\" xmlns:mc=\""u8);
        writer.WriteRawXml(DocxSchema.NsMarkupCompatibility);
        writer.WriteRaw("\">"u8);

        foreach (Comment comment in document.Comments)
        {
            if (!identifiers.TryGetValue(comment, out string? id))
                continue;

            writer.WriteRaw("<w15:commentEx w15:paraId=\""u8);
            writer.WriteAttributeText(id);
            writer.WriteRaw("\""u8);

            if (Parent(document, comment) is { } parent && identifiers.TryGetValue(parent, out string? parentId))
            {
                writer.WriteRaw(" w15:paraIdParent=\""u8);
                writer.WriteAttributeText(parentId);
                writer.WriteRaw("\""u8);
            }

            writer.WriteRaw(comment.IsResolved ? " w15:done=\"1\"/>"u8 : " w15:done=\"0\"/>"u8);
        }

        writer.WriteRaw("</w15:commentsEx>"u8);
    }

    private static Comment? Parent(WordDocument document, Comment comment) =>
        comment.ParentId is { } id ? document.Comments.FirstOrDefault(other => other.Id == id) : null;

    /// <summary>The captured start tag with any identifier of its own removed.</summary>
    private static string? Without(string? attributes)
    {
        if (attributes is null)
            return null;

        int marker = attributes.IndexOf(" w14:paraId=\"", StringComparison.Ordinal);
        if (marker < 0)
            return attributes;

        int end = attributes.IndexOf('"', marker + " w14:paraId=\"".Length);
        return end < 0 ? attributes : attributes[..marker] + attributes[(end + 1)..];
    }

    /// <summary>Pulls <c>w14:paraId</c> out of a captured start tag.</summary>
    private static string? ParagraphId(string? attributes)
    {
        if (attributes is null)
            return null;

        int marker = attributes.IndexOf("paraId=\"", StringComparison.Ordinal);
        if (marker < 0)
            return null;

        int start = marker + "paraId=\"".Length;
        int end = attributes.IndexOf('"', start);
        return end < 0 ? null : attributes[start..end];
    }
}
