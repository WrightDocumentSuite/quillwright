using Inkwright;
using Inkwright.Cos;
using Inkwright.Tagging;
using Quillwright.Pdf.Layout;

namespace Quillwright.Pdf.Render;

/// <summary>
/// Builds the logical structure tree while the pages are drawn.
/// </summary>
/// <remarks>
/// The tree has to mirror reading order, and reading order is the order the composer placed things
/// in, so the elements are created the first time content asks for them rather than up front. A
/// paragraph that spilled onto a second page asks for the same <see cref="TagRef"/> twice and gets
/// the same element, which is how it stays one paragraph in the tree.
/// </remarks>
internal sealed class DocumentTagger : ITagWriter
{
    private readonly PdfExportContext _context;
    private readonly StructureTreeBuilder _builder;
    private readonly Dictionary<TagRef, StructureElement> _elements = [];
    private readonly StructureElement _root;

    internal DocumentTagger(PdfExportContext context)
    {
        _context = context;
        _builder = new StructureTreeBuilder(context.Pdf)
        {
            Language = context.Options.Language
                ?? context.Source.Properties.Language
                ?? context.Source.Styles.DefaultRunFormat.Language,
        };

        _root = _builder.AddRoot("Document");
    }

    /// <inheritdoc />
    public ITagSink ForPage(PdfPage page, ComposedPage composed) => new PageSink(this, page);

    /// <summary>Writes the tree into the document once every page has been drawn.</summary>
    public void Build()
    {
        _builder.Build();
        Scope(_context.Pdf.Structure);
    }

    /// <summary>
    /// Says what each header cell heads. A reader has to know whether a header cell speaks for the
    /// column below it or the row beside it, and a header row speaks for its columns; without that
    /// the cells under it are read out unlabelled.
    /// </summary>
    private static void Scope(IReadOnlyList<PdfStructureNode> nodes)
    {
        foreach (PdfStructureNode node in nodes)
        {
            if (node.Tag == "TH")
            {
                var attributes = new PdfDictionary(2);
                attributes.Set(PdfName.Get("O"), PdfValue.Name(PdfName.Get("Table")));
                attributes.Set(PdfName.Get("Scope"), PdfValue.Name(PdfName.Get("Column")));
                node.Dictionary.Set(PdfName.Get("A"), PdfValue.Dictionary(attributes));
            }

            Scope(node.Children);
        }
    }

    private StructureElement Element(TagRef tag)
    {
        if (_elements.TryGetValue(tag, out StructureElement? existing))
            return existing;

        StructureElement parent = tag.Parent is { } above ? Element(above) : _root;
        StructureElement created = parent.Add(tag.Tag);

        if (!string.IsNullOrEmpty(tag.AlternateText))
            created.AlternateText = tag.AlternateText;

        _elements[tag] = created;
        return created;
    }

    private sealed class PageSink(DocumentTagger tagger, PdfPage page) : ITagSink
    {
        public int Next(TagRef tag)
        {
            int mcid = tagger._builder.NextMarkedContentId(page);
            tagger.Element(tag).AddContent(page.Id, mcid);
            return mcid;
        }
    }
}
