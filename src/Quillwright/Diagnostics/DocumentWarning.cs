namespace Quillwright.Diagnostics;

/// <summary>
/// Machine-readable classification of a recoverable problem found while loading a document.
/// </summary>
public enum WarningCode
{
    /// <summary>A relationship id referenced by the markup has no matching relationship.</summary>
    MissingRelationship,

    /// <summary>A referenced package part is absent.</summary>
    MissingPart,

    /// <summary>An attribute value did not parse as the type the schema calls for.</summary>
    InvalidAttribute,

    /// <summary>A style, numbering or note reference points at something that does not exist.</summary>
    DanglingReference,

    /// <summary>A style chain refers back to itself; the cycle was cut.</summary>
    StyleCycle,

    /// <summary>Markup appeared where the schema does not allow it and was skipped.</summary>
    UnexpectedElement,

    /// <summary>A part could not be parsed and was left out of the model.</summary>
    UnreadablePart,

    /// <summary>An image or embedded object could not be resolved.</summary>
    UnresolvedMedia,

    /// <summary>
    /// A feature was recognised but is not represented in the model. Inside a package the
    /// markup is kept verbatim and comes back on save; in a conversion between formats there
    /// is nowhere to keep it, and the message says what was left behind.
    /// </summary>
    PreservedVerbatim,

    /// <summary>
    /// Content could not be carried into another document — it leans on parts or relationships
    /// of the package it came from — and was left behind. The message says what.
    /// </summary>
    NotCarried,
}

/// <summary>
/// A recoverable problem found while loading a document, reported instead of thrown so that
/// a slightly broken file still opens.
/// </summary>
/// <param name="Code">Machine-readable classification.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="PartPath">Package part the problem was found in, when known.</param>
public readonly record struct DocumentWarning(WarningCode Code, string Message, string? PartPath = null)
{
    /// <inheritdoc />
    public override string ToString() =>
        PartPath is null ? $"{Code}: {Message}" : $"{Code} [{PartPath}]: {Message}";
}
