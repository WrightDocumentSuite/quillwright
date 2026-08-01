namespace Quillwright.Model;

/// <summary>
/// An object embedded in a document: a spreadsheet, a slide, a PDF, or a plain file someone
/// dragged in as an attachment.
/// </summary>
/// <remarks>
/// <para>
/// Reading is one way, as it is for macros. The object is decoded for inspection and its part
/// is copied through untouched on save, so what is read here and what a saved file carries are
/// always the same bytes.
/// </para>
/// <para>
/// What the object actually is depends on the program that made it. A modern one is a package
/// of its own — an <c>.xlsx</c> sitting inside the <c>.docx</c> — and <see cref="Content"/> is
/// that file. An older one is a compound file whose streams describe it ([MS-OLEDS] 1.3.3),
/// and when it wraps a plain file rather than a live object,
/// <see cref="PackagedFile"/> is that file's own bytes.
/// </para>
/// </remarks>
public sealed class EmbeddedObject
{
    /// <summary>Where the object lives: the package part, or the storage inside a legacy file.</summary>
    public required string Location { get; init; }

    /// <summary>
    /// The program that owns the object, as the document names it — <c>Excel.Sheet.12</c>,
    /// <c>Package</c>, <c>AcroExch.Document</c>.
    /// </summary>
    public string? ProgramId { get; init; }

    /// <summary>
    /// What the object calls itself, from its <c>\1CompObj</c> stream ([MS-OLEDS] 2.3.8) —
    /// the phrase a user sees, such as "Microsoft Excel Worksheet".
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>Whether the document links to the object rather than holding it.</summary>
    public bool IsLinked { get; init; }

    /// <summary>The bytes of the object as the document stores them.</summary>
    public ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>
    /// Name of the file the object wraps, when it wraps one rather than being a live object.
    /// </summary>
    public string? PackagedFileName { get; init; }

    /// <summary>The bytes of that file, ready to be written out as it stood.</summary>
    public ReadOnlyMemory<byte> PackagedFile { get; init; }

    /// <summary>The picture the document shows in place of the object, when it caches one.</summary>
    public ImageData? Preview { get; init; }

    /// <summary>Whether the object wraps a plain file that can be extracted as it stood.</summary>
    public bool IsPackagedFile => PackagedFileName is not null;
}
