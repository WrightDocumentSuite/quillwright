using System.Xml;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>
/// Splits the flat block list of <c>w:body</c> into sections.
/// </summary>
/// <remarks>
/// A section ends at the paragraph whose properties carry a <c>w:sectPr</c>, and the last
/// section's properties sit at the end of the body. Collecting blocks until a break appears
/// and then closing a section reproduces exactly that rule, and each section is wired to the
/// header and footer parts its references name.
/// </remarks>
internal sealed class BodyBuffer
{
    private readonly LoadContext _context;
    private readonly List<Block> _pending = [];
    private SectionStart _nextStart = SectionStart.NextPage;

    public BodyBuffer(LoadContext context) => _context = context;

    private SectionProperties? _finalProperties;

    /// <summary>Appends a block, closing a section when the block carries a break.</summary>
    public void Add(Block block)
    {
        _pending.Add(block);
        if (block is Paragraph { SectionBreak: { } properties })
            CloseSection(properties);
    }

    /// <summary>Appends a block element the body reader parses on its own, such as a content control.</summary>
    public void AddElement(XmlReader xml, string name, BodyReader body)
    {
        if (body.ReadBlockElement(xml, name) is { } block)
            Add(block);
    }

    /// <summary>Records the properties of the last section, which the body carries directly.</summary>
    public void SetFinalSection(SectionProperties properties, List<SectionReader.Reference> references)
    {
        properties.LoadedReferences = references;
        _finalProperties = properties;
    }

    /// <summary>Creates the sections on the document.</summary>
    public void Build()
    {
        CloseSection(_finalProperties ?? new SectionProperties());
        if (_context.Document.Sections.Count == 0)
            _context.Document.Sections.Add(new Section());
    }

    private void CloseSection(SectionProperties properties)
    {
        // A sectPr belongs to the section it terminates, except for w:type: OOXML defines that
        // value as the kind of break which starts the following section.  Keep the public model
        // convenient for layout (Start means how this section starts) by carrying the parsed
        // value forward to the next section.
        SectionStart followingStart = properties.Start;
        properties.Start = _nextStart;

        var section = new Section { Properties = properties };
        foreach (Block block in _pending)
        {
            if (block is Paragraph paragraph)
            {
                paragraph.IsSectionBreakCarrier = paragraph.SectionBreak is not null;
                paragraph.SectionBreak = null;
            }
            section.Blocks.Add(block);
        }

        _pending.Clear();
        _context.Document.Sections.Add(section);
        WireHeadersAndFooters(section, properties);
        _nextStart = followingStart;
    }

    private void WireHeadersAndFooters(Section section, SectionProperties properties)
    {
        if (properties.LoadedReferences is not { } references)
            return;

        foreach ((bool isFooter, HeaderFooterKind kind, string relationshipId) in references)
        {
            if (!_context.HeadersByRelationship.TryGetValue(relationshipId, out HeaderFooter? part))
            {
                _context.Warn(Diagnostics.WarningCode.MissingRelationship,
                    $"A section refers to header or footer '{relationshipId}', which has no part.");
                continue;
            }

            if (isFooter)
                section.Footers[kind] = part;
            else
                section.Headers[kind] = part;
        }

        properties.LoadedReferences = null;
    }
}
