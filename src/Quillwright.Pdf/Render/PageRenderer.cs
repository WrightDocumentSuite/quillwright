using Inkwright;
using Inkwright.Annotations;
using Inkwright.Content;
using Inkwright.Cos;
using Quillwright.Pdf.Layout;

namespace Quillwright.Pdf.Render;

/// <summary>
/// Writes composed pages into the document: one PDF page per composed page, one content stream per
/// page, and the annotations that cannot live in a content stream.
/// </summary>
internal sealed class PageRenderer
{
    private readonly PdfExportContext _context;
    private readonly ImageEmbedder _images;
    private readonly PageFieldResolver _fields;
    private readonly CommentRenderer _comments;
    private readonly List<(PdfLinkAnnotation Link, string Anchor)> _internal = [];

    internal PageRenderer(PdfExportContext context, PageFieldResolver fields)
    {
        _context = context;
        _images = new ImageEmbedder(context);
        _fields = fields;
        _comments = new CommentRenderer(context);
    }

    /// <summary>Draws every composed page.</summary>
    /// <param name="pages">The pages, in order.</param>
    /// <param name="bookmarks">Where the bookmarks of the document ended up.</param>
    /// <param name="tags">Where to report marked content, or <see langword="null"/> when untagged.</param>
    public void Render(
        IReadOnlyList<ComposedPage> pages,
        IReadOnlyDictionary<string, BookmarkTarget> bookmarks,
        ITagWriter? tags)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(bookmarks);

        foreach (ComposedPage composed in pages)
            RenderPage(composed, tags);

        Aim(bookmarks);
        _comments.Complete();
    }

    private void RenderPage(ComposedPage composed, ITagWriter? tags)
    {
        PdfPage page = _context.Pdf.Pages.Add(composed.Geometry.Size);
        ITagSink? sink = tags?.ForPage(page, composed);

        using var canvas = ContentCanvas.ForPage(page);
        var painter = new ItemPainter(
            canvas,
            page,
            composed,
            _images,
            _comments,
            field => _fields.Resolve(field, composed),
            (link, anchor) => _internal.Add((link, anchor)),
            sink);

        List<LinkItem> links = [];
        foreach (PageItem item in composed.Furniture)
        {
            if (item is LinkItem link)
                links.Add(link);
            else
                painter.Paint(item);
        }

        foreach (PageItem item in composed.Items)
        {
            if (item is LinkItem link)
                links.Add(link);
            else
                painter.Paint(item);
        }

        painter.PaintLinks(links);

        canvas.Commit();
    }

    /// <summary>
    /// Points the links that lead inside the document at the places they name. A link whose
    /// bookmark is nowhere is left inert rather than pointed at the wrong page.
    /// </summary>
    private void Aim(IReadOnlyDictionary<string, BookmarkTarget> bookmarks)
    {
        foreach ((PdfLinkAnnotation link, string anchor) in _internal)
        {
            link.Uri = null;

            if (!bookmarks.TryGetValue(anchor, out BookmarkTarget target) ||
                target.PageIndex >= _context.Pdf.Pages.Count)
            {
                continue;
            }

            var destination = new PdfArray(5)
            {
                PdfValue.Reference(_context.Pdf.Pages[target.PageIndex].Id),
                PdfValue.Name(PdfName.Get("XYZ")),
                PdfValue.Number(target.X),
                PdfValue.Number(target.Y),
                PdfValue.Null,
            };

            link.Dictionary.Set(PdfName.Get("Dest"), PdfValue.Array(destination));
        }
    }
}

/// <summary>Builds the structure tree as pages are drawn. Implemented only when tagging is on.</summary>
internal interface ITagWriter
{
    /// <summary>Starts collecting the marked content of one page.</summary>
    /// <param name="page">The page being drawn.</param>
    /// <param name="composed">What was composed for it.</param>
    ITagSink ForPage(PdfPage page, ComposedPage composed);
}
