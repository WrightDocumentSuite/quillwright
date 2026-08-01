using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Writes the comments and the threading between them, which are two parts rather than one
/// because replies and resolved state were added to the format long after comments were.
/// </summary>
internal static partial class DocxSaver
{
    private static async ValueTask WriteCommentsAsync(OpcPackage package, WordDocument document, SavePlan plan, CancellationToken cancellationToken)
    {
        if (!plan.Writes(DocxSchema.RelComments, DocxSchema.PartComments, document.Comments.Count > 0))
            return;

        string path = plan.PathFor(DocxSchema.RelComments, DocxSchema.PartComments);
        Utf8XmlWriter writer = await package.CreateXmlPartAsync(path, DocxSchema.ContentTypeComments, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
        {
            document.PartRoots.TryGetValue("comments", out string? rootAttributes);
            WordXml.OpenRoot(writer, "comments"u8, rootAttributes);
            BodyWriteContext context = CreateContext(plan, path);
            foreach (Comment comment in document.Comments)
            {
                writer.WriteRaw("<w:comment"u8);
                WordXml.Attribute(writer, "w:id"u8, comment.Id);
                WordXml.Attribute(writer, "w:author"u8, comment.Author ?? string.Empty);
                if (comment.Date is { } date)
                    WordXml.Attribute(writer, "w:date"u8, date.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture));
                WordXml.Attribute(writer, "w:initials"u8, comment.Initials);
                if (comment.Attributes is { } attributes)
                    writer.WriteRawXml(attributes);
                writer.WriteRaw(">"u8);
                BodyWriter.WriteBlocks(writer, comment.Blocks, context);
                writer.WriteRaw("</w:comment>"u8);
            }

            writer.WriteRaw("</w:comments>"u8);
        }
    }

    /// <summary>
    /// Writes the part that says which comments are replies to which, and which have been
    /// marked resolved. It is written only when there is something to say, because it names
    /// comments by paragraph identifier and giving one to every comment otherwise would
    /// change documents that never asked for threading.
    /// </summary>
    private static async ValueTask WriteCommentThreadsAsync(
        OpcPackage package,
        WordDocument document,
        SavePlan plan,
        Dictionary<Comment, string> threads,
        CancellationToken cancellationToken)
    {
        if (threads.Count == 0)
            return;

        string path = plan.PathFor(DocxSchema.RelCommentsExtended, DocxSchema.PartCommentsExtended);
        Utf8XmlWriter writer = await package.CreateXmlPartAsync(path, DocxSchema.ContentTypeCommentsExtended, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
            CommentThreadWriter.Write(writer, document, threads);
    }

    /// <summary>
    /// Writes the part that gives each comment an identifier that survives renumbering. Only
    /// a package that already carried one gets it back, since it exists for Word's own
    /// co-authoring bookkeeping and inventing it would change documents that never used it.
    /// </summary>
    private static async ValueTask WriteCommentIdsAsync(
        OpcPackage package,
        WordDocument document,
        SavePlan plan,
        IReadOnlyDictionary<Comment, string> threads,
        IReadOnlyDictionary<Comment, string> durable,
        CancellationToken cancellationToken)
    {
        if (!plan.WritesCommentIds || durable.Count == 0)
            return;

        string path = plan.PathFor(DocxSchema.RelCommentsIds, DocxSchema.PartCommentsIds);
        Utf8XmlWriter writer = await package.CreateXmlPartAsync(path, DocxSchema.ContentTypeCommentsIds, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
            CommentIdsPart.Write(writer, document, threads, durable);
    }

    /// <summary>
    /// Writes the part holding a comment's UTC timestamp, its follow-up flag and whatever
    /// extensions — reactions, most of all — it came in with. Walking the model rather than
    /// copying the part through is what drops the entries of comments that are gone.
    /// </summary>
    private static async ValueTask WriteCommentsExtensibleAsync(
        OpcPackage package,
        WordDocument document,
        SavePlan plan,
        IReadOnlyDictionary<Comment, string> durable,
        CancellationToken cancellationToken)
    {
        if (!plan.WritesCommentsExtensible || durable.Count == 0)
            return;

        string path = plan.PathFor(DocxSchema.RelCommentsExtensible, DocxSchema.PartCommentsExtensible);
        Utf8XmlWriter writer = await package
            .CreateXmlPartAsync(path, DocxSchema.ContentTypeCommentsExtensible, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
            CommentExtensiblePart.Write(writer, document, durable);
    }

    /// <summary>
    /// Writes the identities behind the author names, adding any comment author the loaded
    /// part did not already know about.
    /// </summary>
    private static async ValueTask WritePeopleAsync(
        OpcPackage package, WordDocument document, SavePlan plan, CancellationToken cancellationToken)
    {
        if (!plan.WritesPeople)
            return;

        PeoplePart.Reconcile(document);
        string path = plan.PathFor(DocxSchema.RelPeople, DocxSchema.PartPeople);
        Utf8XmlWriter writer = await package
            .CreateXmlPartAsync(path, DocxSchema.ContentTypePeople, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
            PeoplePart.Write(writer, document);
    }
}
