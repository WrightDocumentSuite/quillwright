namespace Quillwright.Pdf.Layout;

/// <summary>
/// A block after it has been measured. A paragraph and a table are laid out very differently but
/// stack the same way, so the composer and the cells that hold them work in terms of this.
/// </summary>
internal abstract class BlockBox
{
    /// <summary>Space above the block, in points.</summary>
    public double SpacingBefore { get; init; }

    /// <summary>Space below the block, in points.</summary>
    public double SpacingAfter { get; init; }

    /// <summary>How tall the block's own content is, without the spacing around it.</summary>
    public abstract double ContentHeight { get; }

    /// <summary>How much room the block wants altogether.</summary>
    public double TotalHeight => SpacingBefore + ContentHeight + SpacingAfter;
}
