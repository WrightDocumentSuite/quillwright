using Inkwright;
using Inkwright.Annotations;
using Quillwright.Model;
using Quillwright.Pdf.Layout;

namespace Quillwright.Pdf.Render;

/// <summary>Turns laid-out Word comment references into interactive PDF annotation threads.</summary>
internal sealed class CommentRenderer
{
    private const string _resolvedMessage =
        "Resolved in the source Word document; resolver identity and time are unavailable.";

    private readonly PdfExportContext _context;
    private readonly HashSet<Comment> _emitted = new(ReferenceEqualityComparer.Instance);
    private Dictionary<int, List<Comment>>? _repliesByParentId;
    private int _resolvedWithoutResolver;

    internal CommentRenderer(PdfExportContext context) => _context = context;

    /// <summary>Writes one root comment and every reply beneath it at its laid-out endpoint.</summary>
    internal void Paint(
        PdfPage page,
        PageGeometry geometry,
        Comment comment,
        double x,
        double y,
        TagRef? tag,
        ITagSink? tags)
    {
        if (comment.ParentId is not null || !_emitted.Add(comment))
            return;

        var point = new PdfPoint(x, geometry.ToPdfY(y));
        PdfTextAnnotation root = page.Annotations.AddNote(
            point,
            Body(comment),
            comment.Author,
            PdfNoteIcon.Comment);

        Describe(root, comment);
        tags?.AddAnnotation(tag, root);

        var path = new HashSet<int> { comment.Id };
        AddReplies(comment, root, tag, tags, path);
        AddResolvedInformation(comment, root, tag, tags);
    }

    /// <summary>Reports malformed or unsupported comments that had no printable anchor.</summary>
    internal void Complete()
    {
        if (!_context.Options.IncludeComments)
            return;

        if (_resolvedWithoutResolver > 0)
        {
            _context.Diagnostics.Add(
                PdfExportWarningKind.ContentSkipped,
                $"{_resolvedWithoutResolver} resolved Word comment message(s) did not record " +
                "the resolver identity required by PDF /State /T; machine-readable Completed " +
                "state was omitted and a provenance-free informational reply was emitted.",
                "comment-resolved-state");
        }

        int missing = _context.Source.Comments.Count(comment => !_emitted.Contains(comment));
        if (missing == 0)
            return;

        _context.Diagnostics.Add(
            PdfExportWarningKind.ContentSkipped,
            $"{missing} comment message(s) had no printable anchored thread and could not become PDF annotations.",
            "comment-anchors");
    }

    private void AddReplies(
        Comment source,
        PdfMarkupAnnotation parent,
        TagRef? tag,
        ITagSink? tags,
        HashSet<int> path)
    {
        var frames = new Stack<ReplyFrame>();
        frames.Push(new ReplyFrame(source, parent, RepliesTo(source.Id), removeFromPath: false));

        while (frames.Count > 0)
        {
            ReplyFrame frame = frames.Peek();
            if (frame.NextChildIndex >= frame.Children.Count)
            {
                frames.Pop();
                if (frame.RemoveFromPath)
                {
                    AddResolvedInformation(frame.Source, frame.Annotation, tag, tags);
                    path.Remove(frame.Source.Id);
                }

                continue;
            }

            Comment reply = frame.Children[frame.NextChildIndex++];
            if (_emitted.Contains(reply) || !path.Add(reply.Id))
                continue;

            _emitted.Add(reply);
            PdfTextAnnotation annotation = frame.Annotation.Reply(Body(reply), reply.Author);
            Describe(annotation, reply);
            tags?.AddAnnotation(tag, annotation);

            frames.Push(new ReplyFrame(reply, annotation, RepliesTo(reply.Id), removeFromPath: true));
        }
    }

    private IReadOnlyList<Comment> RepliesTo(int parentId)
    {
        Dictionary<int, List<Comment>> index = _repliesByParentId ??= IndexReplies();
        return index.TryGetValue(parentId, out List<Comment>? replies) ? replies : [];
    }

    private Dictionary<int, List<Comment>> IndexReplies()
    {
        var index = new Dictionary<int, List<Comment>>();

        // Threading names the parent by comment id. Preserve the comments-part order within each
        // bucket: it is the order the recursive renderer historically used for sibling replies.
        foreach (Comment comment in _context.Source.Comments)
        {
            if (comment.ParentId is not int parentId)
                continue;

            if (!index.TryGetValue(parentId, out List<Comment>? replies))
            {
                replies = [];
                index.Add(parentId, replies);
            }

            replies.Add(comment);
        }

        return index;
    }

    private void AddResolvedInformation(
        Comment source,
        PdfMarkupAnnotation target,
        TagRef? tag,
        ITagSink? tags)
    {
        if (!source.IsResolved)
            return;

        PdfTextAnnotation information = target.Reply(_resolvedMessage);

        // Reply() inherits the parent author and AddNote() supplies current timestamps. Neither is
        // resolution provenance: Word records only the done flag. PDF 32000-1 §12.5.6.3 requires
        // /T to identify the user whenever /State is present, so keep this an ordinary reply and
        // remove all invented provenance instead of writing an invalid anonymous /State reply.
        information.Author = null;
        information.CreatedAt = null;
        information.ModifiedAt = null;
        tags?.AddAnnotation(tag, information);
        _resolvedWithoutResolver++;
    }

    private static void Describe(PdfMarkupAnnotation annotation, Comment comment)
    {
        DateTimeOffset? moment = comment.DateUtc ?? comment.Date;
        annotation.CreatedAt = moment;
        annotation.ModifiedAt = moment;
        annotation.Name = comment.DurableId ?? $"quillwright-comment-{comment.Id}";
        annotation.Subject = comment.IsFollowUp ? "Word follow-up" : "Word comment";
    }

    private static string Body(Comment comment)
    {
        if (comment is { IsFollowUp: true, ParentId: null })
            return "Follow-up";

        string text = comment.GetText();
        return string.IsNullOrWhiteSpace(text) ? "Comment" : text;
    }

    private sealed class ReplyFrame(
        Comment source,
        PdfMarkupAnnotation annotation,
        IReadOnlyList<Comment> children,
        bool removeFromPath)
    {
        internal Comment Source { get; } = source;

        internal PdfMarkupAnnotation Annotation { get; } = annotation;

        internal IReadOnlyList<Comment> Children { get; } = children;

        internal bool RemoveFromPath { get; } = removeFromPath;

        internal int NextChildIndex { get; set; }
    }
}
