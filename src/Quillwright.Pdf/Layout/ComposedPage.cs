using Quillwright.Model;

namespace Quillwright.Pdf.Layout;

/// <summary>One page decided but not yet written: its geometry, its number and what goes on it.</summary>
internal sealed class ComposedPage
{
    internal ComposedPage(PageGeometry geometry, Section section, int index, int number)
    {
        Geometry = geometry;
        Section = section;
        Index = index;
        Number = number;
    }

    /// <summary>The measurements of the page.</summary>
    public PageGeometry Geometry { get; }

    /// <summary>The section whose page setup opened this page.</summary>
    public Section Section { get; }

    /// <summary>The position of the page in the document, counted from zero.</summary>
    public int Index { get; }

    /// <summary>The number printed on the page, which restarts wherever a section says it does.</summary>
    public int Number { get; set; }

    /// <summary>Whether this is the first page of its section.</summary>
    public bool IsSectionStart { get; init; }

    /// <summary>What to draw in the body, in painting order.</summary>
    public List<PageItem> Items { get; } = [];

    /// <summary>What to draw in the header and the footer, kept apart so it can be tagged as an artifact.</summary>
    public List<PageItem> Furniture { get; } = [];
}
