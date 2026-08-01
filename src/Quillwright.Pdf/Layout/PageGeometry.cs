using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// The measurements of one page in points, with the origin at the top-left corner and the vertical
/// axis pointing down.
/// </summary>
/// <remarks>
/// Word measures in twips from the top of the page; PDF measures in points from the bottom. Rather
/// than flipping in a dozen places, the whole layout runs in this top-down space and the renderer
/// flips once, at the moment it writes a coordinate. <see cref="ToPdfY"/> is that one place.
/// </remarks>
internal readonly record struct PageGeometry
{
    private PageGeometry(
        double width,
        double height,
        double marginTop,
        double marginRight,
        double marginBottom,
        double marginLeft,
        double headerDistance,
        double footerDistance,
        double headerHeight,
        double footerHeight)
    {
        Width = width;
        Height = height;
        MarginTop = marginTop;
        MarginRight = marginRight;
        MarginBottom = marginBottom;
        MarginLeft = marginLeft;
        HeaderDistance = headerDistance;
        FooterDistance = footerDistance;
        HeaderHeight = headerHeight;
        FooterHeight = footerHeight;
    }

    /// <summary>The page width in points.</summary>
    public double Width { get; }

    /// <summary>The page height in points.</summary>
    public double Height { get; }

    /// <summary>Space above the body.</summary>
    public double MarginTop { get; }

    /// <summary>Space to the right of the body.</summary>
    public double MarginRight { get; }

    /// <summary>Space below the body.</summary>
    public double MarginBottom { get; }

    /// <summary>Space to the left of the body, gutter included.</summary>
    public double MarginLeft { get; }

    /// <summary>Distance from the top of the page to the top of the header.</summary>
    public double HeaderDistance { get; }

    /// <summary>Distance from the bottom of the page to the bottom of the footer.</summary>
    public double FooterDistance { get; }

    /// <summary>How tall the tallest header of this section is.</summary>
    public double HeaderHeight { get; }

    /// <summary>How tall the tallest footer of this section is.</summary>
    public double FooterHeight { get; }

    /// <summary>The left edge of the body.</summary>
    public double ContentLeft => MarginLeft;

    /// <summary>The right edge of the body.</summary>
    public double ContentRight => Width - MarginRight;

    /// <summary>How wide the body is.</summary>
    public double ContentWidth => Math.Max(0, ContentRight - ContentLeft);

    /// <summary>
    /// The top edge of the body, measured downwards from the top of the page. A header taller
    /// than the top margin pushes the body down rather than being drawn over it.
    /// </summary>
    public double ContentTop => Math.Max(MarginTop, HeaderDistance + HeaderHeight);

    /// <summary>The bottom edge of the body, measured downwards from the top of the page.</summary>
    public double ContentBottom => Height - Math.Max(MarginBottom, FooterDistance + FooterHeight);

    /// <summary>How tall the body is.</summary>
    public double ContentHeight => Math.Max(0, ContentBottom - ContentTop);

    /// <summary>The size to give the PDF page.</summary>
    public Inkwright.PageSize Size => new(Width, Height);

    /// <summary>Converts a distance measured down from the top of the page into a PDF coordinate.</summary>
    /// <param name="y">The distance from the top of the page, in points.</param>
    public double ToPdfY(double y) => Height - y;

    /// <summary>
    /// Reads the geometry of a section. The gutter widens the binding edge, which for a
    /// left-to-right section is the left margin.
    /// </summary>
    /// <param name="properties">The section's page setup.</param>
    /// <param name="mirrorToRight">
    /// Whether this page's gutter belongs on the right, which is what a right-to-left section or a
    /// verso page of a mirrored document asks for.
    /// </param>
    /// <param name="headerHeight">How tall the section's tallest header is.</param>
    /// <param name="footerHeight">How tall the section's tallest footer is.</param>
    public static PageGeometry From(
        SectionProperties properties,
        bool mirrorToRight = false,
        double headerHeight = 0,
        double footerHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(properties);

        PageMargins margins = properties.Margins;
        double gutter = Points(margins.Gutter);
        bool gutterOnRight = mirrorToRight || properties.RightToLeftGutter;

        return new PageGeometry(
            width: Points(properties.PageWidth),
            height: Points(properties.PageHeight),
            marginTop: Points(margins.Top),
            marginRight: Points(margins.Right) + (gutterOnRight ? gutter : 0),
            marginBottom: Points(margins.Bottom),
            marginLeft: Points(margins.Left) + (gutterOnRight ? 0 : gutter),
            headerDistance: Points(margins.Header),
            footerDistance: Points(margins.Footer),
            headerHeight: Math.Max(0, headerHeight),
            footerHeight: Math.Max(0, footerHeight));
    }

    /// <summary>A length in points, which is the unit the whole layout works in.</summary>
    public static double Points(Length length) => length.Points;
}
