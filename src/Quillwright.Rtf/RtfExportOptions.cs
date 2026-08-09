namespace Quillwright.Rtf;

/// <summary>Controls how a document is projected to RTF.</summary>
public sealed record RtfExportOptions
{
    /// <summary>The options used when a caller passes none.</summary>
    public static RtfExportOptions Default { get; } = new();
}
