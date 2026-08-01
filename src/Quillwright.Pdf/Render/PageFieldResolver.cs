using Quillwright.Model;
using Quillwright.Pdf.Layout;
using Quillwright.Styles;

namespace Quillwright.Pdf.Render;

/// <summary>
/// Answers what a page-related field prints, once pagination has settled.
/// </summary>
/// <remarks>
/// A page number is the one thing that cannot be known while a page is being filled: the count of
/// pages depends on the layout, and the layout depends on how wide the count prints. The composer
/// therefore lays out an estimate, and this resolver supplies the truth at render time; the
/// exporter recomposes once if the estimate turned out to be a different width.
/// </remarks>
internal sealed class PageFieldResolver
{
    /// <summary>What Word itself prints for a reference whose bookmark is nowhere.</summary>
    private const string MissingBookmark = "Error! Bookmark not defined.";

    private readonly PdfExportDiagnostics? _diagnostics;
    private readonly Dictionary<Section, int> _sectionPages = [];
    private readonly Dictionary<string, ComposedPage> _bookmarkPages = new(StringComparer.Ordinal);

    internal PageFieldResolver(PdfExportDiagnostics? diagnostics = null) => _diagnostics = diagnostics;

    /// <summary>The number of pages in the document.</summary>
    public int TotalPages { get; private set; }

    /// <summary>
    /// Whether a <c>PAGEREF</c> was estimated before the bookmarks had pages. The estimate was
    /// a guess, so the caller should compose again now that <see cref="Observe"/> has run.
    /// </summary>
    public bool EstimatedBlindPageRef { get; private set; }

    /// <summary>Records how the document actually paginated.</summary>
    /// <param name="pages">The composed pages, in order.</param>
    /// <param name="bookmarks">Where each bookmark ended up.</param>
    public void Observe(IReadOnlyList<ComposedPage> pages, IReadOnlyDictionary<string, BookmarkTarget>? bookmarks = null)
    {
        ArgumentNullException.ThrowIfNull(pages);

        TotalPages = pages.Count;
        EstimatedBlindPageRef = false;
        _sectionPages.Clear();

        foreach (ComposedPage page in pages)
            _sectionPages[page.Section] = _sectionPages.GetValueOrDefault(page.Section) + 1;

        if (bookmarks is null)
            return;

        _bookmarkPages.Clear();
        foreach ((string name, BookmarkTarget target) in bookmarks)
        {
            if (target.PageIndex >= 0 && target.PageIndex < pages.Count)
                _bookmarkPages[name] = pages[target.PageIndex];
        }
    }

    /// <summary>What the field prints on a page.</summary>
    /// <param name="field">The field.</param>
    /// <param name="page">The page it sits on.</param>
    public string Resolve(PageFieldFragment field, ComposedPage page)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(page);

        if (field.Kind == PageFieldKind.PageRef)
        {
            if (field.Bookmark is null || !_bookmarkPages.TryGetValue(field.Bookmark, out ComposedPage? target))
            {
                _diagnostics?.Add(
                    PdfExportWarningKind.ContentSkipped,
                    "A PAGEREF names a bookmark the document does not define, and prints the error Word prints",
                    field.Bookmark ?? "unnamed");
                return MissingBookmark;
            }

            // The reference prints the number the way the page it points at displays it.
            return NumberFormatter.Format(
                target.Number,
                field.FormatStated ? field.Format : target.Section.Properties.PageNumbering.Format ?? field.Format);
        }

        int value = field.Kind switch
        {
            PageFieldKind.Page => page.Number,
            PageFieldKind.NumPages => TotalPages,
            _ => _sectionPages.GetValueOrDefault(page.Section, 1),
        };

        // A field that named no scheme of its own counts the way its section says pages count.
        ListNumberFormat format = field.FormatStated
            ? field.Format
            : page.Section.Properties.PageNumbering.Format ?? field.Format;

        return NumberFormatter.Format(value, format);
    }

    /// <summary>
    /// The value to lay a field out with before the document has been paginated. The page number
    /// is known even then, because the composer only asks about the page it is filling; the page
    /// of a bookmark is known only on a second pass, and asking blind is recorded so the caller
    /// knows a second pass is owed.
    /// </summary>
    /// <param name="kind">Which quantity the field prints.</param>
    /// <param name="pageNumber">The number of the page being filled.</param>
    /// <param name="format">The numbering scheme.</param>
    /// <param name="bookmark">The bookmark a <c>PAGEREF</c> points at.</param>
    public string Estimate(PageFieldKind kind, int pageNumber, ListNumberFormat format, string? bookmark = null)
    {
        if (kind == PageFieldKind.PageRef)
        {
            if (bookmark is not null && _bookmarkPages.TryGetValue(bookmark, out ComposedPage? target))
                return NumberFormatter.Format(target.Number, format);

            EstimatedBlindPageRef = true;
            return NumberFormatter.Format(Math.Max(1, pageNumber), format);
        }

        int value = kind switch
        {
            PageFieldKind.Page => pageNumber,
            PageFieldKind.NumPages => TotalPages > 0 ? TotalPages : pageNumber,
            _ => TotalPages > 0 ? TotalPages : pageNumber,
        };

        return NumberFormatter.Format(Math.Max(1, value), format);
    }
}
